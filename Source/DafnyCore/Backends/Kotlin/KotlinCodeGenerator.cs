using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.IO;
using System.Diagnostics.Contracts;
using System.Collections.ObjectModel;
using System.CommandLine;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using static Microsoft.Dafny.ConcreteSyntaxTreeUtils;

namespace Microsoft.Dafny.Compilers {
  public class KotlinCodeGenerator : SinglePassCodeGenerator {
    public KotlinCodeGenerator(DafnyOptions options, ErrorReporter reporter) : base(options, reporter) {
      IntSelect = ",dafny.BigInteger";
      LambdaExecute = ".invoke";
    }

    public override IReadOnlySet<Feature> UnsupportedFeatures => new HashSet<Feature> {
      Feature.Iterators,
      Feature.SubsetTypeTests,
      Feature.MethodSynthesis,
      Feature.TuplesWiderThan20,
      Feature.ArraysWithMoreThan16Dims,
      Feature.ArrowsWithMoreThan16Arguments,
      Feature.RuntimeCoverageReport,
      // No Kotlin-translated standard library exists yet, so declare it
      // unsupported (like the C++ backend) rather than emitting the Java one.
      Feature.StandardLibraries,
      Feature.StandardLibrariesActionsExterns,
    };

    const string DafnySetClass = "dafny.DafnySet";
    const string DafnyMultiSetClass = "dafny.DafnyMultiset";
    const string DafnySeqClass = "dafny.DafnySequence";
    const string DafnyMapClass = "dafny.DafnyMap";

    const string DafnyBigRationalClass = "dafny.BigRational";
    const string DafnyEuclideanClass = "dafny.DafnyEuclidean";
    const string DafnyHelpersClass = "dafny.Helpers";
    const string DafnyTypeDescriptor = "dafny.TypeDescriptor";
    string FormatDefaultTypeParameterValue(TypeParameter tp) => FormatDefaultTypeParameterValueName(tp.GetCompileName(Options));
    static string FormatDefaultTypeParameterValueName(string tpName) => $"_default_{tpName}";

    const string DafnyFunctionIfacePrefix = "dafny.Function";
    const string DafnyMultiArrayClassPrefix = "dafny.Array";
    const string DafnyTupleClassPrefix = "dafny.Tuple";

    string DafnyMultiArrayClass(int dim) => DafnyMultiArrayClassPrefix + dim;
    string DafnyTupleClass(int size) => DafnyTupleClassPrefix + size;

    string DafnyFunctionIface(int arity) =>
      arity == 1 ? "dafny.Function1" : DafnyFunctionIfacePrefix + arity;

    static string FormatExternBaseClassName(string externClassName) =>
      $"_ExternBase_{externClassName}";
    static string FormatTypeDescriptorVariable(string typeVarName) =>
      $"_td_{typeVarName}";
    string FormatTypeDescriptorVariable(TypeParameter tp) =>
      FormatTypeDescriptorVariable(tp.GetCompileName(Options));

    const string TypeMethodName = "_typeDescriptor";

    private string ModuleName;
    private string ModulePath;
    private readonly List<GenericCompilationInstrumenter> Instrumenters = [];

    public void AddInstrumenter(GenericCompilationInstrumenter compilationInstrumenter) {
      Instrumenters.Add(compilationInstrumenter);
    }

    protected override bool UseReturnStyleOuts(MethodOrConstructor m, int nonGhostOutCount) => true;


    protected override bool SupportsAmbiguousTypeDecl => false;
    protected override bool SupportsProperties => false;

    private enum KotlinNativeType { Byte, Short, Int, Long }

    private static KotlinNativeType AsKotlinNativeType(NativeType.Selection sel) {
      switch (sel) {
        case NativeType.Selection.Byte:
        case NativeType.Selection.SByte:
          return KotlinNativeType.Byte;
        case NativeType.Selection.Short:
        case NativeType.Selection.UShort:
          return KotlinNativeType.Short;
        case NativeType.Selection.Int:
        case NativeType.Selection.UInt:
          return KotlinNativeType.Int;
        case NativeType.Selection.Long:
        case NativeType.Selection.ULong:
          return KotlinNativeType.Long;
        default:
          Contract.Assert(false);
          throw new Cce.UnreachableException();
      }
    }

    private static bool IsUnsignedKotlinNativeType(NativeType nt) {
      Contract.Requires(nt != null);
      switch (nt.Sel) {
        case NativeType.Selection.Byte:
        case NativeType.Selection.UShort:
        case NativeType.Selection.UInt:
        case NativeType.Selection.ULong:
          return true;
        default:
          return false;
      }
    }

    private static KotlinNativeType AsKotlinNativeType(NativeType nt) {
      return AsKotlinNativeType(nt.Sel);
    }

    private KotlinNativeType? AsKotlinNativeType(Type type) {
      var nt = AsNativeType(type);
      if (nt == null) {
        return null;
      } else {
        return AsKotlinNativeType(nt);
      }
    }

    protected override void DeclareSpecificOutCollector(string collectorVarName, ConcreteSyntaxTree wr, List<Type> formalTypes, List<Type> lhsTypes) {
      // If the method returns an array of parameter type, and we're assigning
      // to a variable with a more specific type, we need to insert a cast:
      //
      // Array<Integer> outcollector42 = obj.Method(); // <-- you are here
      // int[] out43 = (int[]) outcollector42.unwrap();
      var returnedTypes = new List<string>();
      Contract.Assert(formalTypes.Count == lhsTypes.Count);
      for (var i = 0; i < formalTypes.Count; i++) {
        var formalType = formalTypes[i];
        var lhsType = lhsTypes[i];
        if (formalType.IsArrayType && formalType.AsArrayType.Dims == 1 && UserDefinedType.ArrayElementType(formalType).IsTypeParameter) {
          returnedTypes.Add("Any");
        } else {
          // Type the out-collector by the method's actual RETURN type (formalType), not
          // the assignment target (lhsType). Kotlin generics are invariant, so a
          // `Tuple<Object,...>` collector won't accept the method's `Tuple<A,...>` result;
          // the per-out `dtor__i() as LhsType` extraction handles any narrowing.
          // EXCEPTION: if formalType is (or CONTAINS, at any depth) the callee's own type
          // parameter, that name isn't in scope at the call site — fall back to the
          // instantiated lhsType.
          var collectorType = (formalType.IsTypeParameter || ContainsTypeParameter(formalType)) ? lhsType : formalType;
          var typeName = formalTypes.Count > 1 ? BoxedTypeName(collectorType, wr, Token.NoToken) : TypeName(collectorType, wr, Token.NoToken);
          returnedTypes.Add(typeName);
        }
      }
      if (formalTypes.Count > 1) {
        // Use a star-projected tuple type for the collector so it accepts the method's
        // exact return tuple regardless of invariance; each out is narrowed by the
        // `dtor__i() as LhsType` extraction that follows.
        var stars = string.Join(", ", System.Linq.Enumerable.Repeat("*", formalTypes.Count));
        wr.Write($"var {collectorVarName}: {DafnyTupleClass(formalTypes.Count)}<{stars}> = ");
      } else {
        wr.Write($"var {collectorVarName}: {returnedTypes[0]} = ");
      }
    }

    // True if `type` mentions a type parameter anywhere (itself or nested in type args).
    private static bool ContainsTypeParameter(Type type) {
      type = type.NormalizeExpand();
      if (type.IsTypeParameter) {
        return true;
      }
      return type.TypeArgs.Any(ContainsTypeParameter);
    }
    protected override void EmitCastOutParameterSplits(string outCollector, List<string> lhsNames,
      ConcreteSyntaxTree wr, List<Type> formalTypes, List<Type> lhsTypes, IOrigin tok) {
      var wOuts = new List<ConcreteSyntaxTree>();
      for (var i = 0; i < lhsNames.Count; i++) {
        wr.Write($"{lhsNames[i]} = ");
        //
        // Suppose we have:
        //
        //   method Foo<A>(a : A) returns (arr : array<A>)
        //
        // This is compiled to:
        //
        //   public <A> Object Foo(A a)
        //
        // (There's also an argument for the type descriptor, but I'm omitting
        // it for clarity.)  Foo returns Object, not A[], since A could be
        // primitive and primitives cannot be generic parameters in Java
        // (*sigh*).  So when we call it:
        //
        //   var arr : int[] := Foo(42);
        //
        // we have to add a type cast:
        //
        //   BigInteger[] arr = (BigInteger[]) Foo(new BigInteger(42));
        //
        // Things can get more complicated than this, however.  If the method returns
        // the array as part of a tuple:
        //
        //   method Foo<A>(a : A) returns (pair : (array<A>, array<A>))
        //
        // then we get:
        //
        //   public <A> Tuple2<Object, Object> Foo(A a)
        //
        // and we have to write:
        //
        //   BigInteger[] arr = (Pair<BigInteger[], BigInteger[]>) (Object) Foo(new BigInteger(42));
        //
        // (Note the extra cast to Object, since Java doesn't allow a cast to
        // change a type parameter, as that's unsound in general.  It just
        // happens to be okay here!)
        //
        // Rather than try and exhaustively check for all the circumstances
        // where a cast is necessary, for the moment we just always cast to the
        // LHS type via Object, which is redundant 99% of the time but not
        // harmful.
        // Kotlin postfix cast. The out-collector / tuple destructor is typed Any (or a
        // type parameter), so cast to the concrete LHS type with 'as'.
        if (lhsNames.Count == 1) {
          wr.Write(outCollector);
        } else {
          wr.Write($"{outCollector}.dtor__{i}()");
        }
        if (lhsTypes[i] != null) {
          var lhsTypeName = TypeName(lhsTypes[i], wr, Token.NoToken);
          // A nullable LHS needs 'as T?'; otherwise 'as T'.
          if (lhsTypes[i] is { IsRefType: true, IsNonNullRefType: false } && !lhsTypeName.EndsWith("?")) {
            lhsTypeName += "?";
          }
          wr.Write($" as {lhsTypeName}");
        }
        EndStmt(wr);
      }
    }

    protected override void EmitSeqSelect(SingleAssignStmt s0, List<Type> tupleTypeArgsList, ConcreteSyntaxTree wr, string tup) {
      wr.Write("(");
      var lhs = (SeqSelectExpr)s0.Lhs;
      EmitIndexCollectionUpdate(lhs.Seq.Type, out var wColl, out var wIndex, out var wValue, wr, nativeIndex: true);
      var wCoerce = EmitCoercionIfNecessary(from: NativeObjectType, to: lhs.Seq.Type, tok: s0.Origin, wr: wColl);
      // Kotlin postfix casts (was Java prefix `(Type)`).
      wCoerce.Write("(");
      EmitTupleSelect(tup, 0, wCoerce);
      wCoerce.Write($" as {TypeName(lhs.Seq.Type.NormalizeExpand(), wCoerce, s0.Origin)})");
      wColl.Write(")");
      var wCast = EmitCoercionToNativeInt(wIndex);
      EmitTupleSelect(tup, 1, wCast);
      wValue.Write("(");
      EmitTupleSelect(tup, 2, wValue);
      wValue.Write($" as {TypeName(tupleTypeArgsList[2].NormalizeExpand(), wValue, s0.Origin)})");
      EndStmt(wr);
    }

    protected override void EmitMultiSelect(SingleAssignStmt s0, List<Type> tupleTypeArgsList, ConcreteSyntaxTree wr, string tup, int L) {
      var arrayType = tupleTypeArgsList[0];
      var rhsType = tupleTypeArgsList[L - 1];

      var lhs = (MultiSelectExpr)s0.Lhs;
      var indices = new List<Action<ConcreteSyntaxTree>>();
      for (var i = 0; i < lhs.Indices.Count; i++) {
        var wIndex = new ConcreteSyntaxTree();
        // Kotlin postfix cast: (tup.dtor__i() as dafny.BigInteger)
        wIndex.Write("(");
        EmitTupleSelect(tup, i + 1, wIndex);
        wIndex.Write(" as dafny.BigInteger)");
        indices.Add(wr => wr.Write(wIndex.ToString()));
      }

      var (wArray, wRhs) = EmitArrayUpdate(indices, rhsType, wr);
      wArray = EmitCoercionIfNecessary(from: null, to: arrayType, tok: s0.Origin, wr: wArray);
      wArray.Write("(");
      EmitTupleSelect(tup, 0, wArray);
      wArray.Write($" as {TypeName(arrayType.NormalizeExpand(), wArray, s0.Origin)})");

      wRhs.Write("(");
      EmitTupleSelect(tup, L - 1, wRhs);
      wRhs.Write($" as {TypeName(rhsType, wr, s0.Origin)})");

      EndStmt(wr);
    }

    protected override void WriteCast(string s, ConcreteSyntaxTree wr) {
      // WriteCast emits only a Java-style prefix cast "(Type)" which Kotlin cannot parse
      // (Kotlin uses the postfix "expr as Type"). Its only use in the base generator is to
      // cast a multi-dimensional array element access to its element type. In this backend
      // arrays are dafny.ArrayN<T> wrappers whose get(...) already returns the element type
      // T, so no cast is needed; emit nothing.
    }

    protected override ConcreteSyntaxTree EmitCast(ICanRender toType, ConcreteSyntaxTree wr) {
      // Kotlin uses a postfix 'as' cast: (expr as Type). For native numeric types,
      // 'as' is invalid (e.g. Int as Byte), so use the postfix conversion method instead.
      // Render the target type to a string (ICanRender has no usable ToString()).
      var typeBuf = new ConcreteSyntaxTree();
      typeBuf.Append(toType);
      var typeStr = typeBuf.ToString().Trim();
      string conv = typeStr switch {
        "Byte" or "kotlin.Byte" or "java.lang.Byte" => ".toByte()",
        "Short" or "kotlin.Short" or "java.lang.Short" => ".toShort()",
        "Int" or "kotlin.Int" or "Integer" or "java.lang.Integer" => ".toInt()",
        "Long" or "kotlin.Long" or "java.lang.Long" => ".toLong()",
        _ => null,
      };
      if (conv != null) {
        wr.Write("(");
        var inner = wr.ForkInParens();
        wr.Write($"){conv}");
        return inner;
      }
      wr.Write("(");
      var w = wr.ForkInParens();
      wr.Format($" as {toType})");
      return w;
    }

    protected override ConcreteSyntaxTree EmitIngredients(ConcreteSyntaxTree wr, string ingredients, int L, string tupleTypeArgs, ForallStmt s, SingleAssignStmt s0, Expression rhs) {
      var wStmts = wr.Fork();
      var wrVarInit = wr;
      wrVarInit.Write($"val {ingredients}: MutableList<{DafnyTupleClass(L)}<{tupleTypeArgs}>> = ");
      Contract.Assert(L <= MaxTupleNonGhostDims);
      EmitEmptyTupleList(tupleTypeArgs, wrVarInit);
      var wrOuter = wr;
      wr = CompileGuardedLoops(s.BoundVars, s.Bounds, s.Range, wr);
      var wrTuple = EmitAddTupleToList(ingredients, tupleTypeArgs, wr);
      wrTuple.Write($"{L}<{tupleTypeArgs}>(");
      if (s0.Lhs is MemberSelectExpr lhs1) {
        wrTuple.Append(Expr(lhs1.Obj, false, wStmts));
      } else if (s0.Lhs is SeqSelectExpr lhs2) {
        wrTuple.Append(Expr(lhs2.Seq, false, wStmts));
        wrTuple.Write(", ");
        TrParenExpr(lhs2.E0, wrTuple, false, wStmts);
      } else {
        var lhs = (MultiSelectExpr)s0.Lhs;
        wrTuple.Append(Expr(lhs.Array, false, wStmts));
        foreach (var t in lhs.Indices) {
          wrTuple.Write(", ");
          TrParenExpr(t, wrTuple, false, wStmts);
        }
      }

      wrTuple.Write(", ");
      if (rhs is MultiSelectExpr) {
        Type t = rhs.Type.NormalizeExpand();
        wrTuple.Write($"({TypeName(t, wrTuple, rhs.Origin)})");
      }

      wrTuple.Append(Expr(rhs, false, wStmts));
      return wrOuter;
    }

    protected override void EmitHeader(Program program, ConcreteSyntaxTree wr) {
      if (Options.IncludeRuntime) {
        EmitRuntimeSource("DafnyRuntimeKotlin", wr);
      }
      // Note: no Kotlin-translated standard library exists yet — declared as an
      // unsupported feature (see UnsupportedFeatures), so nothing is emitted here.
      wr.WriteLine($"// Dafny program {program.Name} compiled into Kotlin");
      ModuleName = program.MainMethod != null ? "main" : Path.GetFileNameWithoutExtension(program.Name);
      wr.WriteLine();
    }

    protected override void EmitBuiltInDecls(SystemModuleManager systemModuleManager, ConcreteSyntaxTree wr) {
      switch (Options.SystemModuleTranslationMode) {
        case CommonOptionBag.SystemModuleMode.Omit: {
            CheckCommonSytemModuleLimits(systemModuleManager);
            return;
          }
        case CommonOptionBag.SystemModuleMode.OmitAllOtherModules: {
            CheckSystemModulePopulatedToCommonLimits(systemModuleManager);
            break;
          }
      }

      foreach (var kv in systemModuleManager.ArrowTypeDecls) {
        var arity = kv.Key;
        CreateLambdaFunctionInterface(arity, wr);
      }

      foreach (var decl in systemModuleManager.SystemModule.TopLevelDecls) {
        if (decl is ArrayClassDecl classDecl) {
          var dims = classDecl.Dims;
          CreateDafnyArrays(dims, wr);
        }
      }
    }

    public static string TransformToClassName(string baseName) {
      baseName = PublicIdProtectAux(baseName);
      var sanitizedName = Regex.Replace(baseName, "[^_A-Za-z0-9$]", "_");
      if (!Regex.IsMatch(sanitizedName, "^[_A-Za-z]")) {
        sanitizedName = "_" + sanitizedName;
      }
      return sanitizedName;
    }

    public override void EmitCallToMain(Method mainMethod, string baseName, ConcreteSyntaxTree wr) {
      // In Kotlin, main function should be at top level
      // Add import for _System module
      wr.WriteLine("import _System.*");
      wr.WriteLine();

      // Don't add module prefix since we have import _System.*
      var companion = TypeName_Companion(UserDefinedType.FromTopLevelDeclWithAllBooleanTypeParameters(mainMethod.EnclosingClass), wr, mainMethod.Origin, mainMethod);

      var wBody = wr.NewNamedBlock("fun main(args: Array<String>)");
      Coverage.EmitSetup(wBody);
      // FromMainArguments returns a covariantly-projected DafnySequence<out DafnySequence<out ...>>,
      // but __Main expects the invariant type, so cast it.
      var mainArgsType = $"dafny.DafnySequence<dafny.DafnySequence<{CharTypeName(true)}>>";
      wBody.WriteLine($"{DafnyHelpersClass}.withHaltHandling {{ {companion}.__Main({DafnyHelpersClass}.{CharMethodQualifier}FromMainArguments(args) as {mainArgsType}) }}");
      Coverage.EmitTearDown(wBody);
    }


    string IdProtectModule(string moduleName) {
      return string.Join(".", moduleName.Split(".").Select(IdProtect));
    }

    protected override ConcreteSyntaxTree CreateModule(ModuleDefinition module, string moduleName, bool isDefault,
      ModuleDefinition externModule,
      string libraryName /*?*/, Attributes moduleAttributes, ConcreteSyntaxTree wr) {
      moduleName = IdProtectModule(moduleName);
      if (isDefault) {
        // Fold the default module into the main module
        moduleName = "_System";
      }
      var pkgName = libraryName ?? IdProtect(moduleName);
      var path = pkgName.Replace('.', '/');
      ModuleName = IdProtect(moduleName);
      ModulePath = path;
      return wr;
    }

    protected override void FinishModule() {
    }

    protected override void DeclareSubsetType(SubsetTypeDecl sst, ConcreteSyntaxTree wr) {
      var cw = (ClassWriter)CreateClass(IdProtect(sst.EnclosingModuleDefinition.GetCompileName(Options)), sst, wr);
      if (sst.WitnessKind == SubsetTypeDecl.WKind.Compiled) {
        var sw = new ConcreteSyntaxTree(cw.InstanceMemberWriter.RelativeIndentLevel);
        var wStmts = cw.InstanceMemberWriter.Fork();
        sw.Append(Expr(sst.Witness, false, wStmts));
        var witness = sw.ToString();
        var typeName = TypeName(sst.Rhs, cw.StaticMemberWriter, sst.Origin);
        if (sst.TypeArgs.Count == 0) {
          cw.DeclareField("Witness", sst, true, true, sst.Rhs, sst.Origin, witness, null);
          witness = "Witness";
        }
        cw.StaticMemberWriter.Write($"fun {TypeParameters(sst.TypeArgs, " ")}defaultValue(");
        var typeDescriptorParams = sst.TypeArgs.Where(NeedsTypeDescriptor);
        cw.StaticMemberWriter.Write(typeDescriptorParams.Comma(TypeDescriptorVariableDeclaration));
        var w = cw.StaticMemberWriter.NewBlock($"): {typeName}");
        w.WriteLine($"return {witness};");
      }

      GenerateIsMethod(sst, cw.StaticMemberWriter);
    }

    private string TypeDescriptorVariableDeclaration(TypeParameter tp) {
      return $"{FormatTypeDescriptorVariable(tp.GetCompileName(Options))}: {DafnyTypeDescriptor}<{tp.GetCompileName(Options)}>";
    }

    protected class ClassWriter : IClassWriter {
      public readonly KotlinCodeGenerator CodeGenerator;
      public readonly ConcreteSyntaxTree InstanceMemberWriter;
      public readonly ConcreteSyntaxTree StaticMemberWriter;
      public readonly ConcreteSyntaxTree CtorBodyWriter;
      // True when this writer represents a trait (interface + _Companion object), as
      // opposed to a concrete class. Trait default methods with a custom receiver live
      // in the _Companion object (StaticMemberWriter). But when a concrete class
      // *inherits* such a method, the redeclaration must be a normal instance method
      // (an `override`), not a companion member — otherwise `this` resolves to the
      // class's Companion. Matches the Java backend, where the inherited method is a
      // plain instance method on the implementing class.
      public readonly bool IsTrait;

      public ClassWriter(KotlinCodeGenerator codeGenerator, ConcreteSyntaxTree instanceMemberWriter, ConcreteSyntaxTree ctorBodyWriter, ConcreteSyntaxTree staticMemberWriter = null, bool isTrait = false) {
        Contract.Requires(codeGenerator != null);
        Contract.Requires(instanceMemberWriter != null);
        this.CodeGenerator = codeGenerator;
        this.InstanceMemberWriter = instanceMemberWriter;
        this.CtorBodyWriter = ctorBodyWriter;
        this.StaticMemberWriter = staticMemberWriter ?? instanceMemberWriter;
        this.IsTrait = isTrait;
      }

      public ConcreteSyntaxTree Writer(bool isStatic, bool createBody, MemberDecl/*?*/ member) {
        if (createBody) {
          if (isStatic || (IsTrait && member != null && member.EnclosingClass is TraitDecl && CodeGenerator.NeedsCustomReceiver(member))) {
            return StaticMemberWriter;
          }
        }
        return InstanceMemberWriter;
      }

      public ConcreteSyntaxTree/*?*/ CreateMethod(MethodOrConstructor m, List<TypeArgumentInstantiation> typeArgs, bool createBody, bool forBodyInheritance, bool lookasideBody) {
        return CodeGenerator.CreateMethod(m, typeArgs, createBody, Writer(m.IsStatic, createBody, m), forBodyInheritance, lookasideBody);
      }

      public ConcreteSyntaxTree SynthesizeMethod(Method m, List<TypeArgumentInstantiation> typeArgs, bool createBody, bool forBodyInheritance, bool lookasideBody) {
        throw new UnsupportedFeatureException(m.Origin, Feature.MethodSynthesis);
      }

      public ConcreteSyntaxTree/*?*/ CreateFunction(string name, List<TypeArgumentInstantiation> typeArgs, List<Formal> formals, Type resultType, IOrigin tok, bool isStatic, bool createBody, MemberDecl member, bool forBodyInheritance, bool lookasideBody) {
        return CodeGenerator.CreateFunction(name, typeArgs, formals, resultType, tok, isStatic, createBody, member, Writer(isStatic, createBody, member), forBodyInheritance, lookasideBody);
      }

      public ConcreteSyntaxTree/*?*/ CreateGetter(string name, TopLevelDecl enclosingDecl, Type resultType, IOrigin tok, bool isStatic, bool isConst, bool createBody, MemberDecl/*?*/ member, bool forBodyInheritance) {
        return CodeGenerator.CreateGetter(name, resultType, tok, isStatic, createBody, member, forBodyInheritance, Writer(isStatic, createBody, member));
      }
      public ConcreteSyntaxTree/*?*/ CreateGetterSetter(string name, Type resultType, IOrigin tok, bool createBody, MemberDecl/*?*/ member, out ConcreteSyntaxTree setterWriter, bool forBodyInheritance) {
        return CodeGenerator.CreateGetterSetter(name, resultType, tok, createBody, out setterWriter, Writer(false, createBody, member), forBodyInheritance);
      }
      public void DeclareField(string name, TopLevelDecl enclosingDecl, bool isStatic, bool isConst, Type type, IOrigin tok, string rhs, Field field) {
        CodeGenerator.DeclareField(name, isStatic, isConst, type, tok, rhs, this);
      }
      public void InitializeField(Field field, Type instantiatedFieldType, TopLevelDeclWithMembers enclosingClass) {
        throw new Cce.UnreachableException();  // InitializeField should be called only for those compilers that set ClassesRedeclareInheritedFields to false.
      }
      public ConcreteSyntaxTree/*?*/ ErrorWriter() => InstanceMemberWriter;

      public void Finish() { }
    }

    protected override bool SupportsStaticsInGenericClasses => false;

    // Like TypeName, but appends `?` for Dafny nullable reference types so that
    // getters/return positions accept null (mirrors the CreateMethod out-type logic).
    private string NullableTypeName(Type type, ConcreteSyntaxTree wr, IOrigin tok) {
      var typeName = TypeName(type, wr, tok);
      if (type is { IsRefType: true, IsNonNullRefType: false } && !typeName.EndsWith("?")) {
        typeName += "?";
      }
      return typeName;
    }

    protected ConcreteSyntaxTree CreateGetter(string name, Type resultType, IOrigin tok, bool isStatic,
      bool createBody, MemberDecl/*?*/ member, bool forBodyInheritance, ConcreteSyntaxTree wr) {
      // A const/field getter that implements a trait member (or is redeclared into a
      // concrete class via body inheritance) overrides the interface getter, so it needs
      // `override` — mirrors CreateMethod. Not for members declared on the trait itself.
      if ((member?.OverriddenMember != null && member.EnclosingClass is not TraitDecl) || forBodyInheritance) {
        wr.Write("override ");
      }
      // Kotlin syntax: fun name(): ReturnType (no static keyword, it's in companion object)
      wr.Write("fun {0}(): {1}", name, NullableTypeName(resultType, wr, tok));
      if (createBody) {
        var w = wr.NewBlock("", null, BlockStyle.NewlineBrace, BlockStyle.NewlineBrace);
        return w;
      } else {
        wr.WriteLine("");  // Abstract methods don't need semicolon in Kotlin
        return null;
      }
    }

    protected override void DeclareLocalVar(string name, Type /*?*/ type, IOrigin /*?*/ tok, Expression rhs,
        bool inLetExprBody, ConcreteSyntaxTree wr) {
      if (type == null) {
        type = rhs.Type;
      }
      var wStmts = wr.Fork();
      var w = DeclareLocalVar(name, type, tok, wr);
      w.Append(Expr(rhs, inLetExprBody, wStmts));
    }

    public ConcreteSyntaxTree /*?*/ CreateGetterSetter(string name, Type resultType, IOrigin tok,
      bool createBody, out ConcreteSyntaxTree setterWriter, ConcreteSyntaxTree wr, bool forBodyInheritance = false) {
      // A getter/setter that redeclares an inherited trait member must be `override`.
      if (forBodyInheritance) { wr.Write("override "); }
      wr.Write($"fun {name}(): {NullableTypeName(resultType, wr, tok)}");
      ConcreteSyntaxTree wGet = null;
      if (createBody) {
        wGet = wr.NewBlock("", null, BlockStyle.NewlineBrace, BlockStyle.NewlineBrace);
      } else {
        wr.WriteLine("");
      }
      if (forBodyInheritance) { wr.Write("override "); }
      wr.Write($"fun set_{name}(value: {NullableTypeName(resultType, wr, tok)})");
      if (createBody) {
        setterWriter = wr.NewBlock("", null, BlockStyle.NewlineBrace, BlockStyle.NewlineBrace);
      } else {
        wr.WriteLine("");
        setterWriter = null;
      }
      return wGet;
    }
    protected ConcreteSyntaxTree CreateMethod(MethodOrConstructor m, List<TypeArgumentInstantiation> typeArgs, bool createBody, ConcreteSyntaxTree wr, bool forBodyInheritance, bool lookasideBody) {
      if (!createBody && (m.IsStatic || m is Constructor)) {
        // No need for an abstract version of a static method or a constructor
        return null;
      }
      string targetReturnTypeReplacement = null;
      int nonGhostOuts = 0;
      int nonGhostIndex = 0;
      for (int i = 0; i < m.Outs.Count; i++) {
        if (!m.Outs[i].IsGhost) {
          nonGhostOuts += 1;
          nonGhostIndex = i;
        }
      }
      if (nonGhostOuts == 1) {
        // If a primitive type is used for a type parameter, it has to be boxed
        var boxed = OutFormalOverridesTypeParameter(m, nonGhostIndex);
        targetReturnTypeReplacement = TypeName(m.Outs[nonGhostIndex].Type, wr, m.Outs[nonGhostIndex].Origin, boxed);
        // Add ? only for Dafny nullable ref types in return types
        if (m.Outs[nonGhostIndex].Type is { IsRefType: true, IsNonNullRefType: false } && !targetReturnTypeReplacement.EndsWith("?")) {
          targetReturnTypeReplacement += "?";
        }
      } else if (nonGhostOuts > 1) {
        // Kotlin requires the type arguments on the tuple type.
        var outTypes = m.Outs.Where(o => !o.IsGhost).Select(o => o.Type).ToList();
        targetReturnTypeReplacement = $"{DafnyTupleClass(nonGhostOuts)}<{BoxedTypeNames(outTypes, wr, m.Origin)}>";
      }
      var customReceiver = createBody && !forBodyInheritance && NeedsCustomReceiver(m);
      var receiverType = UserDefinedType.FromTopLevelDecl(m.Origin, m.EnclosingClass);
      foreach (var instrumenter in Instrumenters) {
        instrumenter.BeforeMethod(m, wr);
      }
      // Kotlin syntax: fun methodName(params): ReturnType
      // For static methods in Kotlin, we output them without any special syntax here
      // since they're already being routed to the companion object writer
      // 'abstract' for a body-less method on a class; 'override' when it overrides a
      // trait/base member (Kotlin interface members are open, so this suffices).
      if (!createBody && !(m.EnclosingClass is TraitDecl)) {
        wr.Write("abstract ");
      } else if ((m.OverriddenMember != null && !(m.EnclosingClass is TraitDecl))
                 || forBodyInheritance) {
        // `forBodyInheritance` means we are redeclaring a method inherited from a trait
        // into a concrete class (a Kotlin instance method delegating to the trait
        // companion); it overrides the interface member, so it needs `override`.
        wr.Write("override ");
      }
      wr.Write("fun ");
      wr.Write(TypeParameters(TypeArgumentInstantiation.ToFormals(ForTypeParameters(typeArgs, m, lookasideBody)), " "));
      wr.Write("{0}(", IdName(m));
      var sep = "";
      WriteRuntimeTypeDescriptorsFormals(ForTypeDescriptors(typeArgs, m.EnclosingClass, m, lookasideBody), wr, ref sep,
        TypeDescriptorVariableDeclaration);
      if (customReceiver) {
        // `_this` is the receiver, which is never null — emit it non-null (bypassing
        // DeclareFormal's nullable-ref-type `?`), else callers hit `Shape?` receivers.
        wr.Write($"{sep}_this: {TypeName(receiverType, wr, m.Origin)}");
        sep = ", ";
      }
      WriteFormals(sep, m.Ins, wr);
      // Kotlin return type comes after parameters
      if (targetReturnTypeReplacement != null) {
        wr.Write("): {0}", targetReturnTypeReplacement);
      } else {
        wr.Write(")");  // Unit return type is implicit in Kotlin
      }
      if (!createBody) {
        wr.WriteLine("");  // No semicolon in Kotlin
        return null; // We do not want to write a function body, so instead of returning a BTW, we return null.
      } else {
        return wr.NewBlock("", null, BlockStyle.NewlineBrace, BlockStyle.NewlineBrace);
      }
    }

    private bool OutFormalOverridesTypeParameter(MethodOrConstructor m, int outIndex) {
      if (m.Outs[outIndex].Type.IsTypeParameter) {
        return true;
      }
      if (m.OverriddenMethod == null) {
        return false;
      }

      return OutFormalOverridesTypeParameter(m.OverriddenMethod, outIndex);
    }

    protected override ConcreteSyntaxTree EmitMethodReturns(MethodOrConstructor m, ConcreteSyntaxTree wr) {
      int nonGhostOuts = 0;
      foreach (var t in m.Outs) {
        if (t.IsGhost) {
          continue;
        }

        nonGhostOuts += 1;
        break;
      }
      if (!m.Body.Body.OfType<ReturnStmt>().Any() && (nonGhostOuts > 0 || m.IsTailRecursive)) { // If method has out parameters or is tail-recursive but no explicit return statement in Dafny
        // The body must run before the implicit `return <outs>`. Emitting the body inside
        // an `if(true) { ... }` wrapper (as Java does) hides its assignments from Kotlin's
        // definite-assignment analysis, so non-null out-vars read at the return look
        // possibly-unassigned. Instead, emit the body linearly into a fork and place a
        // BARE return after it — assignments flow to the return, and Kotlin sees the
        // function returns on all paths. (Unreachable code after a terminal body is only a
        // Kotlin warning, not an error, so no `if(true)` guard is needed as in Java.)
        var bodyWriter = wr.Fork();
        EmitReturn(m.Outs, wr);
        return bodyWriter;
      }
      return wr;
    }

    protected ConcreteSyntaxTree/*?*/ CreateFunction(string name, List<TypeArgumentInstantiation> typeArgs,
      List<Formal> formals, Type resultType, IOrigin tok, bool isStatic, bool createBody, MemberDecl member,
      ConcreteSyntaxTree wr, bool forBodyInheritance, bool lookasideBody) {
      if (!createBody && isStatic) {
        // No need for abstract version of static method
        return null;
      }
      var customReceiver = createBody && !forBodyInheritance && NeedsCustomReceiver(member);
      var receiverType = UserDefinedType.FromTopLevelDecl(member.Origin, member.EnclosingClass);
      // Kotlin syntax: fun name(params): ReturnType
      if (!createBody && !(member.EnclosingClass is TraitDecl)) {
        wr.Write("abstract ");
      } else if ((member.OverriddenMember != null && !(member.EnclosingClass is TraitDecl))
                 || forBodyInheritance) {
        // A body-inheritance redeclaration of a trait function overrides the interface member.
        wr.Write("override ");
      }
      wr.Write("fun ");
      wr.Write(TypeParameters(TypeArgumentInstantiation.ToFormals(ForTypeParameters(typeArgs, member, lookasideBody)), " "));
      wr.Write($"{name}(");
      var sep = "";
      var argCount = WriteRuntimeTypeDescriptorsFormals(ForTypeDescriptors(typeArgs, member.EnclosingClass, member, lookasideBody), wr, ref sep, TypeDescriptorVariableDeclaration);
      if (customReceiver) {
        // `_this` is the receiver, which is never null — emit it non-null.
        wr.Write($"{sep}_this: {TypeName(receiverType, wr, tok)}");
        sep = ", ";
        argCount++;
      }
      argCount += WriteFormals(sep, formals, wr);
      // Kotlin return type after parameters
      var returnTypeName = TypeName(resultType, wr, tok);
      // Add ? only for Dafny nullable ref types in return types
      if (resultType is { IsRefType: true, IsNonNullRefType: false } && !returnTypeName.EndsWith("?")) {
        returnTypeName += "?";
      }
      wr.Write("): {0}", returnTypeName);
      if (!createBody) {
        wr.WriteLine("");  // No semicolon in Kotlin
        return null; // We do not want to write a function body, so instead of returning a BTW, we return null.
      } else {
        ConcreteSyntaxTree w;
        if (argCount > 1) {
          w = wr.NewBlock("", null, BlockStyle.NewlineBrace, BlockStyle.NewlineBrace);
        } else {
          w = wr.NewBlock("");
        }
        return w;
      }
    }

    protected void DeclareField(string name, bool isStatic, bool isConst, Type type, IOrigin tok, string rhs, ClassWriter cw) {
      // Kotlin syntax: val/var name: Type = value
      if (isStatic) {
        var r = rhs ?? DefaultValue(type, cw.StaticMemberWriter, tok);
        var t = StripTypeParameters(TypeName(type, cw.StaticMemberWriter, tok));
        // Add ? only for Dafny nullable ref types in static fields
        if (type is { IsRefType: true, IsNonNullRefType: false } && !t.EndsWith("?")) {
          t += "?";
        }
        var modifier = isConst ? "val" : "var";
        cw.StaticMemberWriter.WriteLine($"{modifier} {name}: {t} = {r}");
      } else {
        Contract.Assert(cw.CtorBodyWriter != null, "Unexpected instance field");
        var typeName = TypeName(type, cw.InstanceMemberWriter, tok);
        var initValue = rhs ?? PlaceboValue(type, cw.CtorBodyWriter, tok);
        // Add ? only for genuinely-nullable Dafny ref types (`array?`, `Class?`), NOT
        // for non-null ref types like `array<T>` — those get a real placebo value and
        // must stay non-null so member access (.dim0/.set/.get) doesn't require `!!`.
        if (type is { IsRefType: true, IsNonNullRefType: false } && !typeName.EndsWith("?")) {
          typeName += "?";
        }
        // Kotlin disallows assigning a literal `null` to a non-null type (incl. type
        // parameters). When the placebo initializer is `null`, declare the field nullable.
        if (rhs == null && initValue == "null" && !typeName.EndsWith("?")) {
          typeName += "?";
        }
        cw.InstanceMemberWriter.WriteLine("var {1}: {0}", typeName, name);
        cw.CtorBodyWriter.WriteLine("this.{0} = {1}", name, initValue);
      }
    }

    private string StripTypeParameters(string s) {
      Contract.Requires(s != null);
      return Regex.Replace(s, @"<.+>", "");
    }

    private void EmitSuppression(ConcreteSyntaxTree wr) {
      // Kotlin uses @Suppress instead of @SuppressWarnings
      wr.WriteLine("@Suppress(\"UNCHECKED_CAST\", \"DEPRECATION\")");
    }

    string TypeParameters(List<TypeParameter>/*?*/ targs, string suffix = "") {
      Contract.Requires(targs == null || Cce.NonNullElements(targs));
      Contract.Ensures(Contract.Result<string>() != null);

      if (targs == null || targs.Count == 0) {
        return "";  // ignore suffix
      }
      return $"<{Util.Comma(targs, IdName)}>{suffix}";
    }

    internal override string TypeName(Type type, ConcreteSyntaxTree wr, IOrigin tok, MemberDecl/*?*/ member = null) {
      // Ensure TypeName never includes "?" - this should only be added in variable declaration contexts
      var typeName = TypeName(type, wr, tok, boxed: false, member);
      // Strip "?" if present (should not be there from TypeNameImpl, but be defensive)
      if (typeName.EndsWith("?")) {
        typeName = typeName.Substring(0, typeName.Length - 1);
      }
      return typeName;
    }

    private string BoxedTypeName(Type type, ConcreteSyntaxTree wr, IOrigin tok) {
      // For Kotlin, use the boxed TypeName
      var typeName = TypeName(type, wr, tok, boxed: true);
      return typeName;
    }

    private string ActualTypeArgument(Type type, TypeParameter.TPVariance variance, ConcreteSyntaxTree wr, IOrigin tok) {
      Contract.Requires(type != null);
      Contract.Requires(wr != null);
      Contract.Requires(tok != null);
      var typeName = BoxedTypeName(type, wr, tok);
      if (variance == TypeParameter.TPVariance.Co) {
        return "" + typeName;
      } else if (variance == TypeParameter.TPVariance.Contra) {
        if (type.IsRefType) {
          return "? super " + typeName;
        }
      }
      return typeName;
    }

    private string BoxedTypeNames(List<Type> types, ConcreteSyntaxTree wr, IOrigin tok) {
      return Util.Comma(types, t => BoxedTypeName(t, wr, tok));
    }

    protected override string TypeArgumentName(Type type, ConcreteSyntaxTree wr, IOrigin tok) {
      return BoxedTypeName(type, wr, tok);
    }

    private string TypeName(Type type, ConcreteSyntaxTree wr, IOrigin tok, bool boxed, MemberDecl /*?*/ member = null) {
      var typeName = TypeNameImpl(type, wr, tok, boxed, false, member);
      // Don't add ? here - only add in specific contexts like variable declarations
      return typeName;
    }

    private string CharTypeName(bool boxed) {
      if (UnicodeCharEnabled) {
        return boxed ? "dafny.CodePoint" : "Int";
      } else {
        return boxed ? "Char" : "Char";
      }
    }

    private string TypeNameImpl(Type type, ConcreteSyntaxTree wr, IOrigin tok, bool boxed, bool erased, MemberDecl/*?*/ member = null) {
      Contract.Ensures(Contract.Result<string>() != null);
      Contract.Assume(type != null);  // precondition; this ought to be declared as a Requires in the superclass

      var xType = DatatypeWrapperEraser.SimplifyType(Options, type);
      if (xType is BoolType) {
        // Kotlin doesn't have primitive boolean, always use Boolean
        return "Boolean";
      } else if (xType is CharType) {
        return CharTypeName(boxed);
      } else if (xType is IntType or BigOrdinalType) {
        return "dafny.BigInteger";
      } else if (xType is RealType) {
        return DafnyBigRationalClass;
      } else if (xType is BitvectorType) {
        var t = (BitvectorType)xType;
        return t.NativeType != null ? GetNativeTypeName(t.NativeType, boxed) : "dafny.BigInteger";
      } else if (member == null && xType.AsNewtype != null) {
        var newtypeDecl = xType.AsNewtype;
        if (newtypeDecl.NativeType is { } nativeType) {
          return GetNativeTypeName(nativeType, boxed);
        }
        return TypeNameImpl(newtypeDecl.ConcreteBaseType(xType.TypeArgs), wr, tok, boxed, erased);
      } else if (xType.IsObjectQ) {
        // Kotlin's top type is Any (nullable Any? for object?), not Java's Object.
        return "Any?";
      } else if (xType.IsArrayType) {
        ArrayClassDecl at = xType.AsArrayType;
        Contract.Assert(at != null);  // follows from type.IsArrayType
        Type elType = UserDefinedType.ArrayElementType(xType);
        return ArrayTypeName(elType, at.Dims, wr, tok, erased);
      } else if (xType is UserDefinedType udt) {
        if (udt.ResolvedClass is TypeParameter tp) {
          if (thisContext != null && thisContext.ParentFormalTypeParametersToActuals.TryGetValue(tp, out var instantiatedTypeParameter)) {
            return TypeName(instantiatedTypeParameter, wr, tok, true, member);
          }
        }
        var s = FullTypeName(udt, member);
        if (s.Equals("string")) {
          return "String";
        }
        var cl = udt.ResolvedClass;
        if (cl is TupleTypeDecl tupleDecl) {
          s = DafnyTupleClass(tupleDecl.NonGhostDims);
        }
        // When accessing a static member, leave off the type arguments
        if (member != null) {
          return TypeName_UDT(s, [], [], wr, udt.Origin, erased);
        } else {
          return TypeName_UDT(s, udt, wr, udt.Origin, erased);
        }
      } else if (xType is SetType) {
        var argType = ((SetType)xType).Arg;
        if (erased) {
          return DafnySetClass;
        }
        return $"{DafnySetClass}<{ActualTypeArgument(argType, TypeParameter.TPVariance.Co, wr, tok)}>";
      } else if (xType is SeqType) {
        var argType = ((SeqType)xType).Arg;
        if (erased) {
          return DafnySeqClass;
        }
        return $"{DafnySeqClass}<{ActualTypeArgument(argType, TypeParameter.TPVariance.Co, wr, tok)}>";
      } else if (xType is MultiSetType) {
        var argType = ((MultiSetType)xType).Arg;
        if (erased) {
          return DafnyMultiSetClass;
        }
        return $"{DafnyMultiSetClass}<{ActualTypeArgument(argType, TypeParameter.TPVariance.Co, wr, tok)}>";
      } else if (xType is MapType) {
        var domType = ((MapType)xType).Domain;
        var ranType = ((MapType)xType).Range;
        if (erased) {
          return DafnyMapClass;
        }
        return $"{DafnyMapClass}<{ActualTypeArgument(domType, TypeParameter.TPVariance.Co, wr, tok)}, {ActualTypeArgument(ranType, TypeParameter.TPVariance.Co, wr, tok)}>";
      } else {
        Contract.Assert(false); throw new Cce.UnreachableException();  // unexpected type
      }
    }

    string ArrayTypeName(Type elType, int dims, ConcreteSyntaxTree wr, IOrigin tok, bool erased) {
      elType = DatatypeWrapperEraser.SimplifyType(Options, elType);
      // All Dafny arrays (1D and multi-dim) map to the runtime dafny.ArrayN<T> wrapper
      // classes, which provide get/set/dimN and work uniformly for both reference and
      // primitive element types in Kotlin.
      if (erased) {
        return DafnyMultiArrayClass(dims);
      } else {
        return $"{DafnyMultiArrayClass(dims)}<{ActualTypeArgument(elType, TypeParameter.TPVariance.Non, wr, tok)}>";
      }
    }

    protected string CollectionTypeUnparameterizedName(CollectionType ct) {
      if (ct is SeqType) {
        return DafnySeqClass;
      } else if (ct is SetType) {
        return DafnySetClass;
      } else if (ct is MultiSetType) {
        return DafnyMultiSetClass;
      } else if (ct is MapType) {
        return DafnyMapClass;
      } else {
        Contract.Assert(false);  // unexpected collection type
        throw new Cce.UnreachableException();  // to please the compiler
      }
    }

    protected override string FullTypeName(UserDefinedType udt, MemberDecl /*?*/ member = null) {
      return FullTypeName(udt, member, false);
    }

    protected string FullTypeName(UserDefinedType udt, MemberDecl member, bool useCompanionName) {
      Contract.Requires(udt != null);
      if (udt.IsBuiltinArrowType) {
        return DafnyFunctionIface(udt.TypeArgs.Count - 1);
      }

      if (member != null && member.IsExtern(Options, out var qualification, out _) && qualification != null) {
        return qualification;
      }
      var cl = udt.ResolvedClass;
      if (cl is NonNullTypeDecl nntd) {
        cl = nntd.Class;
      }
      if (cl is TypeParameter) {
        return IdProtect(udt.GetCompileName(Options));
      } else if (cl is TupleTypeDecl tupleDecl) {
        return DafnyTupleClass(tupleDecl.NonGhostDims);
      } else if (cl is TraitDecl && useCompanionName) {
        return IdProtect(udt.GetFullCompanionCompileName(Options));
      } else if (cl.EnclosingModuleDefinition.GetCompileName(Options) == ModuleName || cl.EnclosingModuleDefinition.TryToAvoidName) {
        return IdProtect(cl.GetCompileName(Options));
      } else {
        return IdProtectModule(cl.EnclosingModuleDefinition.GetCompileName(Options)) + "." + IdProtect(cl.GetCompileName(Options));
      }
    }

    protected override void TypeName_SplitArrayName(Type type, out Type innermostElementType, out string brackets) {
      Contract.Requires(type != null);

      // Dafny arrays map to the runtime dafny.ArrayN<T> wrapper, never to a bracketed
      // native array type, so there is nothing to split off.
      innermostElementType = type;
      brackets = "";
    }

    protected override string TypeNameArrayBrackets(int dims) {
      return "";
    }

    protected override bool DeclareFormal(string prefix, string name, Type type, IOrigin tok, bool isInParam, ConcreteSyntaxTree wr) {
      if (!isInParam) {
        return false;
      }

      var typeName = TypeName(type, wr, tok);
      // Add ? only for Dafny *nullable* reference types in Kotlin.
      if (type.IsRefType && !type.IsNonNullRefType && !typeName.EndsWith("?")) {
        typeName += "?";
      }
      wr.Write($"{prefix}{name}: {typeName}");
      return true;
    }

    protected override string TypeName_UDT(string fullCompileName, List<TypeParameter.TPVariance> variance, List<Type> typeArgs,
      ConcreteSyntaxTree wr, IOrigin tok, bool omitTypeArguments) {
      Contract.Assume(fullCompileName != null);  // precondition; this ought to be declared as a Requires in the superclass
      Contract.Assume(variance != null);  // precondition; this ought to be declared as a Requires in the superclass
      Contract.Assume(typeArgs != null);  // precondition; this ought to be declared as a Requires in the superclass
      Contract.Assume(variance.Count == typeArgs.Count);

      // Check if this is an arrow type (function type) - they have a specific name pattern
      if ((fullCompileName == "Function" || fullCompileName.StartsWith("dafny.Function")) && typeArgs.Count > 0) {
        // Kotlin function type syntax: (T1, T2, ...) -> TReturn
        // The last type argument is the return type
        var paramTypes = typeArgs.Take(typeArgs.Count - 1);
        var returnType = typeArgs[typeArgs.Count - 1];

        var paramTypeNames = paramTypes.Select(t => ActualTypeArgument(t, TypeParameter.TPVariance.Co, wr, tok)).ToList();
        var returnTypeName = ActualTypeArgument(returnType, TypeParameter.TPVariance.Co, wr, tok);

        if (paramTypeNames.Count == 0) {
          return $"() -> {returnTypeName}";
        } else if (paramTypeNames.Count == 1) {
          return $"({paramTypeNames[0]}) -> {returnTypeName}";
        } else {
          return $"({string.Join(", ", paramTypeNames)}) -> {returnTypeName}";
        }
      }

      string s = IdProtect(fullCompileName);
      if (typeArgs.Count != 0 && !omitTypeArguments) {
        s += "<" + BoxedTypeNames(typeArgs, wr, tok) + ">";
      }
      return s;
    }

    // We write an extern class as a base class that the actual extern class
    // needs to extend, so the extern methods and functions need to be abstract
    // in the base class
    protected override bool IncludeExternallyImportedMembers => true;

    //
    // An example to show how type parameters are dealt with:
    //
    //   class Class<T /* needs auto-initializer */, U /* does not */> {
    //     private String sT; // type descriptor for T
    //
    //     // Fields are assigned in the constructor because some will
    //     // depend on a type parameter
    //     public T t;
    //     public U u;
    //
    //     public Class(String sT) {
    //       this.sT = sT;
    //       this.t = dafny.Helpers.getDefault(sT);
    //       // Note: The field must be assigned a real value before being read!
    //       this.u = null;
    //     }
    //
    //     public __ctor(U u) {
    //       this.u = u;
    //     }
    //   }
    //
    protected override IClassWriter CreateClass(string moduleName, bool isExtern, string /*?*/ fullPrintName,
      List<TypeParameter> typeParameters, TopLevelDecl cls, List<Type> /*?*/ superClasses, IOrigin tok, ConcreteSyntaxTree wr) {
      var name = IdName(cls);
      var javaName = isExtern ? FormatExternBaseClassName(name) : name;
      var filename = $"{ModulePath}/{javaName}.kt";
      var w = wr.NewFile(filename);
      w.WriteLine($"// Class {javaName}");
      w.WriteLine($"// Dafny class {name} compiled into Kotlin");
      w.WriteLine($"package {ModuleName};");
      w.WriteLine();
      //TODO: Fix implementations so they do not need this suppression
      EmitSuppression(w);
      foreach (var instrumenter in Instrumenters) {
        instrumenter.BeforeClass(cls, w);
      }
      // __default classes (DefaultClassDecl) become Kotlin singleton 'object's.
      // Generated code calls __default.Main(...) etc. as if static. An object has
      // no type parameters and no constructor; members go directly in the body and
      // are effectively static.
      var isObject = javaName == "__default";
      var abstractness = isExtern ? "abstract " : "";
      var keyword = isObject ? "object" : $"{abstractness}class";
      w.Write($"{keyword} {javaName}{(isObject ? "" : TypeParameters(typeParameters))}");
      string sep;
      // Since Kotlin does not support multiple inheritance, we are assuming a list of "superclasses" is a list of interfaces
      if (superClasses != null) {
        sep = " : ";
        foreach (var trait in superClasses) {
          if (!trait.IsObject) {
            w.Write($"{sep}{TypeName(trait, w, tok)}");
            sep = ", ";
          }
        }
      }
      var wBody = w.NewBlock("");
      var wTypeFields = wBody.Fork();

      ConcreteSyntaxTree wCtorBody;
      if (isObject) {
        // No constructor for a Kotlin object.
        wCtorBody = wBody.Fork();
      } else {
        wBody.Write($"constructor(");
        var wCtorParams = wBody.Fork();
        wCtorBody = wBody.NewBlock(")", "");
        EmitTypeDescriptorsForClass(typeParameters, cls, wTypeFields, wCtorParams, null, wCtorBody);
      }

      // Create companion object for static members (but NOT for __default objects,
      // whose members are already static by virtue of being in an object).
      // Kotlin companion object is like Java's static members
      ConcreteSyntaxTree wCompanion;
      if (isObject) {
        wCompanion = wBody.Fork();
      } else {
        wBody.WriteLine();
        wBody.WriteLine("companion object {");
        wCompanion = wBody.Fork();
        wBody.WriteLine("}");
      }

      // make sure the (static fields associated with the) type method come after the Witness static field
      var wTypeMethod = wCompanion;  // Static type descriptor methods go in companion object
      var wRestOfBody = wBody.Fork();
      if (cls is DefaultClassDecl || (
            (cls is ClassLikeDecl and not ArrayClassDecl) &&
            !Options.Get(KotlinBackend.LegacyDataConstructors))) {
        // don't emit a type-descriptor method
      } else {
        EmitTypeDescriptorMethod(cls, typeParameters, null, null, wTypeMethod);
      }

      if (fullPrintName != null) {
        // By emitting a toString() method, printing an object will give the same output as with other target languages.
        var wToString = wBody.NewBlock("override fun toString(): String");
        wToString.WriteLine($"return \"{fullPrintName}\"");
      }

      return new ClassWriter(this, wRestOfBody, wCtorBody, wCompanion);
    }

    /// <summary>
    /// For each type parameter X in "typeParametersForClass" that needs a type descriptor,
    ///   * Write "protected TypeDescriptor<X> _td_X;" to wTypeFields
    ///     -- each entry is terminated by a newline
    ///   * Write "TypeDescriptor<X> _td_X" to wCtorParams
    ///     -- entries are separated by a comma
    ///   * Write "_td_X" to wCallArguments
    ///     -- entries are separated by a comma
    ///   * Write "this._td_X := _td_X;" to wCtorBody
    ///     -- each entry is terminated by a newline
    /// Any of the writer parameters may be null, so long as at least one is non-null.
    /// The method returns the number type descriptors written.
    /// </summary>
    int EmitTypeDescriptorsForClass(List<TypeParameter> typeParametersForClass, TopLevelDecl cls,
      [CanBeNull] ConcreteSyntaxTree wTypeFields, [CanBeNull] ConcreteSyntaxTree wCtorParams,
      [CanBeNull] ConcreteSyntaxTree wCallArguments, [CanBeNull] ConcreteSyntaxTree wCtorBody,
      string namePrefix = null) {

      namePrefix ??= "";

      var wError = wTypeFields ?? wCtorParams ?? wCallArguments ?? wCtorBody;
      int numberOfEmittedTypeDescriptors = 0;
      if (typeParametersForClass != null) {
        var sep = "";
        foreach (var ta in TypeArgumentInstantiation.ListFromFormals(typeParametersForClass)) {
          if (NeedsTypeDescriptor(ta.Formal)) {
            var fieldName = FormatTypeDescriptorVariable(ta.Formal.GetCompileName(Options));
            var paramName = TypeDescriptor(ta.Actual, wError, ta.Formal.Origin);
            var decl = $"{fieldName}: {DafnyTypeDescriptor}<{namePrefix}{BoxedTypeName(ta.Actual, wError, ta.Formal.Origin)}>";

            wTypeFields?.WriteLine($"protected val {fieldName}: {DafnyTypeDescriptor}<{namePrefix}{BoxedTypeName(ta.Actual, wError, ta.Formal.Origin)}>");
            if (ta.Formal.Parent == cls) {
              wCtorParams?.Write($"{sep}{decl}");
            }
            wCtorBody?.WriteLine($"this.{fieldName} = {paramName};");
            wCallArguments?.Write($"{sep}{paramName}");

            sep = ", ";
            numberOfEmittedTypeDescriptors++;
          }
        }
      }
      return numberOfEmittedTypeDescriptors;
    }

    /// <summary>
    /// Generate the "_typeDescriptor" method for a generated class.
    /// "enclosingType" is allowed to be "null", in which case the target values are assumed to be references.
    /// If "enclosingType" is null, then "targetTypeName" is expected to be the name of the Java type representing the type.
    /// If "enclosingType" is non-null, then "targetTypeName" is expected to be null.
    /// </summary>
    private void EmitTypeDescriptorMethod([CanBeNull] TopLevelDecl enclosingTypeDecl, List<TypeParameter> typeParams, string targetTypeName,
      [CanBeNull] string initializer, ConcreteSyntaxTree wr) {
      Contract.Requires((enclosingTypeDecl != null) != (targetTypeName != null));

      string typeDescriptorExpr;
      if (enclosingTypeDecl == null) {
        Contract.Assert(targetTypeName != null);
        // use reference type
        typeDescriptorExpr = $"{DafnyTypeDescriptor}.referenceWithInitializer {{ {initializer ?? "null"} }}";
      } else {
        Contract.Assert(targetTypeName == null);
        var enclosingTypeWithItsOwnTypeArguments = UserDefinedType.FromTopLevelDecl(enclosingTypeDecl.Origin, enclosingTypeDecl);
        var targetType = DatatypeWrapperEraser.SimplifyTypeAndTrimSubsetTypes(Options, enclosingTypeWithItsOwnTypeArguments);
        var targetTypeIgnoringConstraints = DatatypeWrapperEraser.SimplifyType(Options, enclosingTypeWithItsOwnTypeArguments).GetRuntimeType();
        targetTypeName = BoxedTypeName(targetTypeIgnoringConstraints, wr, enclosingTypeDecl.Origin);
        var w = (enclosingTypeDecl as RedirectingTypeDecl)?.Witness != null ? "Witness" : null;
        switch (AsKotlinNativeType(targetType)) {
          case KotlinNativeType.Byte:
            typeDescriptorExpr = $"{DafnyTypeDescriptor}.byteWithDefault({w ?? "0.toByte()"})";
            break;
          case KotlinNativeType.Short:
            typeDescriptorExpr = $"{DafnyTypeDescriptor}.shortWithDefault({w ?? "0.toShort()"})";
            break;
          case KotlinNativeType.Int:
            typeDescriptorExpr = $"{DafnyTypeDescriptor}.intWithDefault({w ?? "0"})";
            break;
          case KotlinNativeType.Long:
            typeDescriptorExpr = $"{DafnyTypeDescriptor}.longWithDefault({w ?? "0L"})";
            break;
          case null:
            if (targetTypeIgnoringConstraints.IsBoolType) {
              typeDescriptorExpr = $"{DafnyTypeDescriptor}.booleanWithDefault({w ?? "false"})";
            } else if (targetTypeIgnoringConstraints.IsCharType) {
              if (UnicodeCharEnabled) {
                // In unicode mode a char value is already an Int codepoint (see the char->int
                // conversion), so pass it directly — `.code` (a Kotlin Char property) would be
                // an unresolved reference on the Int witness. The no-witness default is the
                // codepoint 0, not the Kotlin char literal 'D'.
                typeDescriptorExpr = $"{DafnyTypeDescriptor}.unicodeCharWithDefault({w ?? "0"})";
              } else {
                typeDescriptorExpr = $"{DafnyTypeDescriptor}.charWithDefault({w ?? CharType.DefaultValueAsString})";
              }
            } else {
              var d = initializer ?? DefaultValue(targetType, wr, enclosingTypeDecl.Origin, true);
              // Reflection-free: the reference descriptor no longer needs a Class token. For a
              // concrete type we just build a reference descriptor with the default initializer;
              // for a type parameter we thread the element type descriptor through.
              var tp = targetTypeIgnoringConstraints.AsTypeParameter;
              if (tp == null) {
                typeDescriptorExpr = $"{DafnyTypeDescriptor}.referenceWithInitializer<{targetTypeName}> {{ {d} }}";
              } else {
                var td = FormatTypeDescriptorVariable(tp.GetCompileName(Options));
                typeDescriptorExpr = $"{DafnyTypeDescriptor}.referenceWithInitializerAndTypeDescriptor<{targetTypeName}>({td}) {{ {d} }}";
              }
            }
            break;
          default:
            Contract.Assert(false); // unexpected case
            throw new Cce.UnreachableException();
        }
      }

      if (typeParams.Count == 0) {
        // `by lazy` so this may forward-reference a `Witness` companion val declared
        // later in the class body (Kotlin initializes non-lazy vals in declaration order).
        wr.WriteLine($"private val _TYPE: {DafnyTypeDescriptor}<{targetTypeName}> by lazy {{ {typeDescriptorExpr} as {DafnyTypeDescriptor}<{targetTypeName}> }}");
        typeDescriptorExpr = "_TYPE";
      }
      wr.Write($"fun {TypeParameters(typeParams, " ")}{TypeMethodName}(");
      EmitTypeDescriptorsForClass(typeParams, enclosingTypeDecl, null, wr, null, null);
      var wTypeMethodBody = wr.NewBlock($"): {DafnyTypeDescriptor}<{targetTypeName}>", "");
      wTypeMethodBody.WriteLine($"return {typeDescriptorExpr} as {DafnyTypeDescriptor}<{targetTypeName}>");
    }

    private string CastIfSmallNativeType(Type t) {
      var nt = AsNativeType(t);
      return nt == null ? "" : CastIfSmallNativeType(nt);
    }

    private string CastIfSmallNativeType(NativeType nt) {
      // Kotlin has no C-style prefix casts; truncation is done with postfix conversions
      // (see NativeTruncationSuffix). Prefix casts therefore emit nothing.
      return "";
    }

    // Postfix conversion needed to truncate an (Int-promoted) arithmetic result back to
    // a small native Kotlin type. Returns "" for Int/Long (no truncation needed).
    private string NativeTruncationSuffix(Type t) {
      var nt = AsNativeType(t);
      return nt == null ? "" : NativeTruncationSuffix(nt);
    }

    private string NativeTruncationSuffix(NativeType nt) {
      switch (AsKotlinNativeType(nt)) {
        case KotlinNativeType.Byte: return ".toByte()";
        case KotlinNativeType.Short: return ".toShort()";
        default: return "";
      }
    }

    // Kotlin postfix conversion to a native type (e.g. .toInt()). Works for any Number,
    // including dafny.BigInteger, via Kotlin stdlib extensions.
    private string NativeConversionMethod(NativeType nt) {
      switch (AsKotlinNativeType(nt)) {
        case KotlinNativeType.Byte: return ".toByte()";
        case KotlinNativeType.Short: return ".toShort()";
        case KotlinNativeType.Int: return ".toInt()";
        case KotlinNativeType.Long: return ".toLong()";
        default: Contract.Assert(false); throw new Cce.UnreachableException();
      }
    }

    private static string TranslateEscapes(string s) {
      s = Util.ReplaceNullEscapesWithCharacterEscapes(s);

      s = Util.UnicodeEscapesToUtf16Escapes(s);

      // Java \u escapes are translated before parsing, so we need to convert to escapes
      // that aren't for characters that will mess up paring the string or character literal.
      s = Util.ReplaceTokensWithEscapes(s, Util.Utf16Escape, match => {
        return match.Value switch {
          "\\u000a" => "\\n",
          "\\u000d" => "\\r",
          "\\u0022" => "\\\"",
          "\\u0027" => "\\\'",
          "\\u005c" => "\\\\",
          _ => match.Value
        };
      });

      return s;
    }

    protected override void EmitLiteralExpr(ConcreteSyntaxTree wr, LiteralExpr e) {
      if (e is StaticReceiverExpr) {
        wr.Write(TypeName(e.Type, wr, e.Origin));
      } else if (e.Value == null) {
        // In Kotlin, we can simply write null for nullable types
        wr.Write("null");
      } else if (e.Value is bool value) {
        wr.Write(value ? "true" : "false");
      } else if (e is CharLiteralExpr) {
        var v = (string)e.Value;
        if (UnicodeCharEnabled) {
          // In unicode mode a char is represented as an Int code point.
          var codePoint = Util.UnescapedCharacters(Options, v, false).Single();
          wr.Write($"{codePoint}");
        } else {
          wr.Write($"'{TranslateEscapes(v)}'");
        }
      } else if (e is StringLiteralExpr str) {
        wr.Write(UnicodeCharEnabled ? $"{DafnySeqClass}.asUnicodeString(" : $"{DafnySeqClass}.asString(");
        TrStringLiteral(str, wr);
        wr.Write(")");
      } else if (AsNativeType(e.Type) is { } nativeType) {
        EmitNativeIntegerLiteral((BigInteger)e.Value, nativeType, wr);
      } else if (e.Value is BigInteger i) {
        if (i.IsZero) {
          wr.Write("dafny.BigInteger.ZERO");
        } else if (i.IsOne) {
          wr.Write("dafny.BigInteger.ONE");
        } else if (long.MinValue < i && i <= long.MaxValue) {
          wr.Write($"dafny.BigInteger.valueOf({i}L)");
        } else {
          // Excludes exactly long.MinValue: Kotlin parses `-9223372036854775808L` as the
          // negation of the (out-of-range) positive literal 9223372036854775808L, so emit
          // it (and anything outside the Long range) via the String constructor.
          wr.Write($"dafny.BigInteger.of(\"{i}\")");
        }
      } else if (e.Value is BaseTypes.BigDec n) {
        if (0 <= n.Exponent) {
          wr.Write($"{DafnyBigRationalClass}(dafny.BigInteger.of(\"{n.Mantissa}");
          for (int j = 0; j < n.Exponent; j++) {
            wr.Write("0");
          }
          wr.Write("\"), dafny.BigInteger.ONE)");
        } else {
          wr.Write($"{DafnyBigRationalClass}(");
          wr.Write($"dafny.BigInteger.of(\"{n.Mantissa}\")");
          wr.Write(", dafny.BigInteger.of(\"1");
          for (int j = n.Exponent; j < 0; j++) {
            wr.Write("0");
          }
          wr.Write("\"))");
        }
      } else {
        Contract.Assert(false); throw new Cce.UnreachableException();  // unexpected literal
      }
    }

    protected override void EmitStringLiteral(string str, bool isVerbatim, ConcreteSyntaxTree wr) {
      if (!isVerbatim) {
        wr.Write($"\"{TranslateEscapes(str)}\"");
      } else {
        // Verbatim strings are emitted char-by-char (as in the Go/JS backends).
        // Kotlin does have raw string literals ("""..."""); switching to them is a
        // possible future simplification.
        var n = str.Length;
        wr.Write("\"");
        for (var i = 0; i < n; i++) {
          if (str[i] == '\"' && i + 1 < n && str[i + 1] == '\"') {
            wr.Write("\\\"");
            i++;
          } else if (str[i] == '\\') {
            wr.Write("\\\\");
          } else if (str[i] == '\n') {
            wr.Write("\\n");
          } else if (str[i] == '\r') {
            wr.Write("\\r");
          } else {
            wr.Write(str[i]);
          }
        }
        wr.Write("\"");
      }
    }

    void EmitNativeIntegerLiteral(BigInteger value, NativeType nt, ConcreteSyntaxTree wr) {
      GetNativeInfo(nt.Sel, out var name, out var literalSuffix, out _);
      var intValue = value;
      if (intValue > long.MaxValue) {
        // The value must be a 64-bit unsigned integer, since it has a native
        // type and unsigned long is the biggest native type
        Contract.Assert(intValue <= ulong.MaxValue);

        // Represent the value as a signed 64-bit integer
        intValue -= ulong.MaxValue + BigInteger.One;
      } else if (nt.Sel == NativeType.Selection.UInt && intValue > int.MaxValue) {
        // Represent the value as a signed 32-bit integer
        intValue -= uint.MaxValue + BigInteger.One;
      }
      // For Byte/Short there are no literal suffixes in Kotlin; emit an Int literal and
      // truncate with a postfix conversion.
      switch (AsKotlinNativeType(nt)) {
        case KotlinNativeType.Byte: wr.Write($"({intValue}).toByte()"); break;
        case KotlinNativeType.Short: wr.Write($"({intValue}).toShort()"); break;
        case KotlinNativeType.Long when intValue == long.MinValue:
          // Kotlin can't parse `-9223372036854775808L` (it's the negation of an
          // out-of-range positive literal); use the named constant instead.
          wr.Write("kotlin.Long.MIN_VALUE");
          break;
        default: wr.Write($"{intValue}{literalSuffix}"); break;
      }
    }

    protected string GetNativeDefault(NativeType nt) {
      switch (AsKotlinNativeType(nt)) {
        case KotlinNativeType.Byte: return "0.toByte()";
        case KotlinNativeType.Short: return "0.toShort()";
        case KotlinNativeType.Int: return "0";
        case KotlinNativeType.Long: return "0L";
        default:
          Contract.Assert(false);  // unexpected native type
          throw new Cce.UnreachableException();  // to please the compiler
      }
    }

    protected override void GetNativeInfo(NativeType.Selection sel, out string name, out string literalSuffix,
      out bool needsCastAfterArithmetic) {
      literalSuffix = "";
      needsCastAfterArithmetic = false;
      // Kotlin has no separate primitive/boxed names; use the boxed (capitalized) ones.
      // Fully-qualify with `kotlin.` so a user-defined Dafny type named Long/Int/etc.
      // can't shadow the built-in native type.
      switch (AsKotlinNativeType(sel)) {
        case KotlinNativeType.Byte: name = "kotlin.Byte"; needsCastAfterArithmetic = true; break;
        case KotlinNativeType.Short: name = "kotlin.Short"; needsCastAfterArithmetic = true; break;
        case KotlinNativeType.Int: name = "kotlin.Int"; break;
        case KotlinNativeType.Long: name = "kotlin.Long"; literalSuffix = "L"; break;
        default:
          Contract.Assert(false);  // unexpected native type
          throw new Cce.UnreachableException();  // to please the compiler
      }
    }

    private string GetNativeTypeName(NativeType nt, bool boxed = false) {
      // In Kotlin the boxed and unboxed names are identical.
      return GetBoxedNativeTypeName(nt);
    }

    private string GetBoxedNativeTypeName(NativeType nt) {
      // Fully-qualify with `kotlin.` so a user Dafny type named Long/Int/etc. can't shadow it.
      switch (AsKotlinNativeType(nt)) {
        case KotlinNativeType.Byte: return "kotlin.Byte";
        case KotlinNativeType.Short: return "kotlin.Short";
        case KotlinNativeType.Int: return "kotlin.Int";
        case KotlinNativeType.Long: return "kotlin.Long";
        default:
          Contract.Assert(false);  // unexpected native type
          throw new Cce.UnreachableException();  // to please the compiler
      }
    }

    // Note the (semantically iffy) distinction between a *primitive type*,
    // being one of the eight Java primitive types, and a NativeType, which can
    // only be one of the integer types.
    // Note also that in --unicode-char mode, we have our own CodePoint boxing type
    // that boxes int values that are actually Dafny char values.
    private bool IsJavaPrimitiveType(Type type) {
      return type.IsBoolType || type.IsCharType || AsNativeType(type) != null;
    }

    protected override void EmitThis(ConcreteSyntaxTree wr, bool callToInheritedMember) {
      var custom =
        (enclosingMethod != null && (enclosingMethod.IsTailRecursive || NeedsCustomReceiver(enclosingMethod))) ||
        (enclosingFunction != null && (enclosingFunction.IsTailRecursive || NeedsCustomReceiver(enclosingFunction))) ||
        (thisContext is NewtypeDecl && !callToInheritedMember) ||
        thisContext is TraitDecl;
      wr.Write(custom ? "_this" : "this");
    }

    protected override void DeclareLocalVar(string name, Type /*?*/ type, IOrigin /*?*/ tok, bool leaveRoomForRhs,
      string /*?*/ rhs, ConcreteSyntaxTree wr) {
      // Note that type can be null to represent the native object type.
      // See comment on NativeObjectType.
      if (type is { IsTypeParameter: true }) {
        EmitSuppression(wr);
      }

      // Kotlin requires type annotations; locals may be left uninitialized as long as
      // they are definitely assigned before use (which Dafny guarantees).
      var typeName = type != null ? TypeName(type, wr, tok) : "Any?";

      // Only a Dafny *nullable* reference type (e.g. Counter?) maps to a Kotlin nullable
      // type. Non-null Dafny ref types map to non-null Kotlin types, so that member access
      // works without ?. or !!.
      var isNullable = type != null && type.IsRefType && !type.IsNonNullRefType;
      if (isNullable && !typeName.EndsWith("?")) {
        typeName += "?";
      }

      wr.Write("var {0}: {1}", name, typeName);
      if (leaveRoomForRhs) {
        Contract.Assert(rhs == null); // follows from precondition
      } else if (rhs != null) {
        wr.WriteLine($" = {rhs}");
      } else if (type is { IsIntegerType: true }) {
        wr.WriteLine(" = dafny.BigInteger.ZERO");
      } else if (isNullable) {
        wr.WriteLine(" = null");
      } else {
        // Non-null reference (or other) type: leave uninitialized; Kotlin's definite
        // assignment analysis allows this since Dafny assigns before first read.
        wr.WriteLine("");
      }
    }

    protected override void DeclareLocalVar(string name, Type /*?*/ type, IOrigin /*?*/ tok, bool leaveRoomForRhs,
      string /*?*/ rhs, ConcreteSyntaxTree wr, Type t) {
      DeclareLocalVar(name, t, tok, leaveRoomForRhs, rhs, wr);
    }

    protected override void EmitCollectionDisplay(CollectionType ct, IOrigin tok, List<Expression> elements,
        bool inLetExprBody, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      if (elements.Count == 0) {
        wr.Write($"{CollectionTypeUnparameterizedName(ct)}.empty<{BoxedTypeName(ct.Arg, wr, tok)}>(");
        if (ct is SeqType) {
          wr.Write(TypeDescriptor(ct.Arg, wr, tok));
        }
        wr.Write(")");
        return;
      }
      wr.Write($"{CollectionTypeUnparameterizedName(ct)}.of<{BoxedTypeName(ct.Arg, wr, tok)}>(");
      string sep = "";
      // The Kotlin runtime's DafnySequence.of(type, vararg) always takes the element
      // TypeDescriptor first (no primitive-specialized overloads like the Java runtime).
      if (ct is SeqType) {
        wr.Write(TypeDescriptor(ct.Arg, wr, tok));
        sep = ", ";
      }

      if (elements.Count != 0) {
        wr.Write(sep);
      }
      TrExprList(elements, wr, inLetExprBody, wStmts, typeAt: _ => NativeObjectType, parens: false);

      wr.Write(")");
    }

    protected override void EmitMapDisplay(MapType mt, IOrigin tok, List<MapDisplayEntry> elements, bool inLetExprBody,
        ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      wr.Write($"{DafnyMapClass}.fromElements(");
      var tuple2 = DafnyTupleClass(2);
      string sep = "";
      foreach (MapDisplayEntry p in elements) {
        wr.Write(sep);
        wr.Write($"{tuple2}(");
        var coercedW = EmitCoercionIfNecessary(from: p.A.Type, to: NativeObjectType, tok: p.A.Origin, wr: wr);
        coercedW.Append(Expr(p.A, inLetExprBody, wStmts));
        wr.Write(", ");
        coercedW = EmitCoercionIfNecessary(from: p.B.Type, to: NativeObjectType, tok: p.B.Origin, wr: wr);
        coercedW.Append(Expr(p.B, inLetExprBody, wStmts));
        wr.Write(")");
        sep = ", ";
      }
      wr.Write(")");
    }

    protected override void GetSpecialFieldInfo(SpecialField.ID id, object idParam, Type receiverType, out string compiledName, out string preString, out string postString) {
      compiledName = "";
      preString = "";
      postString = "";
      switch (id) {
        case SpecialField.ID.UseIdParam:
          compiledName = IdProtect((string)idParam);
          break;
        case SpecialField.ID.ArrayLength:
        case SpecialField.ID.ArrayLengthInt:
          // All arrays are dafny.ArrayN<T> wrappers with .dimI fields. A 1D array (idParam
          // null) uses .dim0.
          compiledName = "dim" + (idParam == null ? 0 : (int)idParam);
          if (id == SpecialField.ID.ArrayLength) {
            // dimI is an Int; BigInteger.valueOf takes a Long.
            preString = "dafny.BigInteger.valueOf((" + preString;
            postString = postString + ").toLong())";
          }
          break;
        case SpecialField.ID.Floor:
          compiledName = "ToBigInteger()";
          break;
        case SpecialField.ID.IsLimit:
          preString = "dafny.BigOrdinal.IsLimit(";
          postString = ")";
          break;
        case SpecialField.ID.IsSucc:
          preString = "dafny.BigOrdinal.IsSucc(";
          postString = ")";
          break;
        case SpecialField.ID.Offset:
          preString = "dafny.BigOrdinal.Offset(";
          postString = ")";
          break;
        case SpecialField.ID.IsNat:
          preString = "dafny.BigOrdinal.IsNat(";
          postString = ")";
          break;
        case SpecialField.ID.Keys:
          compiledName = "keySet()";
          break;
        case SpecialField.ID.Values:
          compiledName = "valueSet()";
          break;
        case SpecialField.ID.Items:
          var mapType = receiverType.NormalizeToAncestorType().AsMapType;
          Contract.Assert(mapType != null);
          var errorWr = new ConcreteSyntaxTree();
          compiledName = $"<{BoxedTypeName(mapType.Domain, errorWr, Token.NoToken)}, {BoxedTypeName(mapType.Range, errorWr, Token.NoToken)}>entrySet()";
          break;
        case SpecialField.ID.Reads:
          compiledName = "_reads";
          break;
        case SpecialField.ID.Modifies:
          compiledName = "_modifies";
          break;
        case SpecialField.ID.New:
          compiledName = "_new";
          break;
        default:
          Contract.Assert(false); // unexpected ID
          break;
      }
    }

    protected override ILvalue EmitMemberSelect(Action<ConcreteSyntaxTree> obj, Type objType, MemberDecl member, List<TypeArgumentInstantiation> typeArgs, Dictionary<TypeParameter, Type> typeMap,
      Type expectedType, string/*?*/ additionalCustomParameter, bool internalAccess = false) {
      var memberStatus = DatatypeWrapperEraser.GetMemberStatus(Options, member);
      if (memberStatus == DatatypeWrapperEraser.MemberCompileStatus.Identity) {
        return SimpleLvalue(obj);
      } else if (memberStatus == DatatypeWrapperEraser.MemberCompileStatus.AlwaysTrue) {
        return SimpleLvalue(w => w.Write("true"));
      } else if (member is SpecialField sf && !(member is ConstantField)) {
        GetSpecialFieldInfo(sf.SpecialId, sf.IdParam, objType, out var compiledName, out _, out _);
        if (compiledName.Length != 0) {
          if (member.EnclosingClass is DatatypeDecl) {
            if (member.EnclosingClass is TupleTypeDecl && sf.Type.Subst(typeMap).IsCharType && UnicodeCharEnabled) {
              return SuffixLvalue(obj, $".{compiledName}().value()");
            } else {
              return SuffixLvalue(obj, $".{compiledName}()");
            }
          } else {
            return SuffixLvalue(obj, $".{compiledName}");
          }
        } else {
          // Assume it's already handled by the caller
          return SimpleLvalue(obj);
        }
      } else if (member is Function fn) {
        return EmitFunctionSelect(obj, member, typeArgs, typeMap, additionalCustomParameter, fn);
      } else {
        var field = (Field)member;
        ILvalue lvalue;
        if (member.IsStatic) {
          lvalue = SimpleLvalue(w => {
            w.Write("{0}.{1}(", TypeName_Companion(objType, w, member.Origin, member), IdName(member));
            EmitTypeDescriptorsActuals(ForTypeDescriptors(typeArgs, member.EnclosingClass, member, false), member.Origin, w);
            w.Write(")");
          });
        } else if (NeedsCustomReceiverNotTrait(member)) {
          // instance const in a newtype
          Contract.Assert(typeArgs.Count == 0);
          lvalue = SimpleLvalue(w => {
            w.Write("{0}.{1}(", TypeName_Companion(objType, w, member.Origin, member), IdName(member));
            obj(w);
            w.Write(")");
          });
        } else if (internalAccess && (member is ConstantField || member.EnclosingClass is TraitDecl)) {
          lvalue = SuffixLvalue(obj, $".{InternalFieldPrefix}{member.GetCompileName(Options)}");
        } else if (internalAccess) {
          lvalue = SuffixLvalue(obj, $".{IdName(member)}");
        } else if (member is ConstantField) {
          lvalue = SimpleLvalue(w => {
            obj(w);
            w.Write(".{0}(", IdName(member));
            EmitTypeDescriptorsActuals(ForTypeDescriptors(typeArgs, member.EnclosingClass, member, false), member.Origin, w);
            w.Write(")");
          });
        } else if (member.EnclosingClass is TraitDecl) {
          lvalue = GetterSetterLvalue(obj, IdName(member), $"set_{IdName(member)}");
        } else {
          lvalue = SuffixLvalue(obj, $".{IdName(member)}");
        }
        return CoercedLvalue(lvalue, field.Type, expectedType);
      }
    }

    private ILvalue EmitFunctionSelect(Action<ConcreteSyntaxTree> obj, MemberDecl member, List<TypeArgumentInstantiation> typeArgs, Dictionary<TypeParameter, Type> typeMap,
      string additionalCustomParameter, Function fn) {
      var wr = new ConcreteSyntaxTree();
      EmitNameAndActualTypeArgs(IdName(member), TypeArgumentInstantiation.ToActuals(ForTypeParameters(typeArgs, member, false)),
        member.Origin, null, false, wr);
      var needsEtaConversion = typeArgs.Any()
                               || additionalCustomParameter != null
                               || (UnicodeCharEnabled &&
                                   (fn.ResultType.IsCharType || fn.Ins.Any(f => f.Type.IsCharType)));
      if (!needsEtaConversion) {
        var nameAndTypeArgs = wr.ToString();
        return SuffixLvalue(obj, $"::{nameAndTypeArgs}");
      } else {
        // We need an eta conversion to adjust for the difference in arity or coerce inputs/outputs.
        // Kotlin lambda: { a0: T0, a1: T1, ... -> obj.F(rtd0, ..., additionalCustomParameter, a0, ...) }
        // (Java emitted `(T0 a0, ...) -> obj.F(...)`, which is not valid Kotlin.)
        wr.Write("(");
        var sep = "";
        EmitTypeDescriptorsActuals(ForTypeDescriptors(typeArgs, member.EnclosingClass, member, false), fn.Origin, wr, ref sep);
        if (additionalCustomParameter != null) {
          wr.Write("{0}{1}", sep, additionalCustomParameter);
          sep = ", ";
        }
        var prefixWr = new ConcreteSyntaxTree();
        var prefixSep = "";
        foreach (var arg in fn.Ins) {
          if (!arg.IsGhost) {
            var name = idGenerator.FreshId("_eta");
            var ty = arg.Type.Subst(typeMap);
            prefixWr.Write($"{prefixSep}{name}: {BoxedTypeName(ty, prefixWr, arg.Origin)}");
            wr.Write(sep);
            var coercedWr = EmitCoercionIfNecessary(NativeObjectType, ty, arg.Origin, wr);
            coercedWr.Write(name);
            sep = ", ";
            prefixSep = ", ";
          }
        }
        prefixWr.Write(" -> ");
        wr.Write(")");

        if (fn.ResultType.IsCharType && UnicodeCharEnabled) {
          prefixWr.Write("dafny.CodePoint.valueOf(");
          wr.Write(")");
        }

        // Emit `{ params -> obj.method(args) }` directly. Writing the pieces verbatim
        // avoids EnclosedLvalue's string.Format treatment (which required brace-doubling
        // and produced malformed `{{ ... }` lambdas).
        var prefixStr = prefixWr.ToString();
        var suffixStr = wr.ToString();
        return SimpleLvalue(w => {
          w.Write("{ ");
          w.Write(prefixStr);
          obj(w);
          w.Write(".");
          w.Write(suffixStr);
          w.Write(" }");
        });
      }
    }

    protected override void EmitConstructorCheck(string source, DatatypeCtor ctor, ConcreteSyntaxTree wr) {
      wr.Write($"{source}.is_{ctor.GetCompileName(Options)}()");
    }

    internal override string TypeName_Companion(Type type, ConcreteSyntaxTree wr, IOrigin tok, MemberDecl/*?*/ member) {
      type = UserDefinedType.UpcastToMemberEnclosingType(type, member);
      if (type is UserDefinedType udt) {
        var name = udt.ResolvedClass is TraitDecl ? udt.GetFullCompanionCompileName(Options) : FullTypeName(udt, member, true);
        return TypeName_UDT(name, udt, wr, tok, true);
      } else {
        return TypeName(type, wr, tok, member);
      }
    }

    protected override ConcreteSyntaxTree EmitArraySelect(List<Action<ConcreteSyntaxTree>> indices, Type elmtType, ConcreteSyntaxTree wr) {
      Contract.Assert(indices != null && 1 <= indices.Count);  // follows from precondition
      var w = EmitArraySelect(indices.Count, out var wIndices, elmtType, wr);
      for (int i = 0; i < indices.Count; i++) {
        var stringifiedIndex = new ConcreteSyntaxTree();
        indices[i](stringifiedIndex);
        var index = stringifiedIndex.ToString();
        if (!int.TryParse(index, out _)) {
          wIndices[i].Write($"{DafnyHelpersClass}.toInt({index})");
        } else {
          wIndices[i].Write(index);
        }
      }
      return w;
    }

    protected override ConcreteSyntaxTree EmitArraySelect(List<Expression> indices, Type elmtType, bool inLetExprBody,
        ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      Contract.Assert(indices != null && 1 <= indices.Count);  // follows from precondition
      var w = EmitArraySelect(indices.Count, out var wIndices, elmtType, wr);

      for (int i = 0; i < indices.Count; i++) {
        TrParenExprAsInt(indices[i], wIndices[i], inLetExprBody, wStmts);
      }

      return w;
    }

    private ConcreteSyntaxTree EmitArraySelect(int dimCount, out List<ConcreteSyntaxTree> wIndices, Type elmtType, ConcreteSyntaxTree wr) {
      elmtType = DatatypeWrapperEraser.SimplifyType(Options, elmtType);
      wIndices = [];
      // All arrays are dafny.ArrayN<T> wrappers: use .get(i0, i1, ...).
      var w = wr.Fork();
      wr.Write(".get(");
      for (int i = 0; i < dimCount; i++) {
        if (i > 0) {
          wr.Write(", ");
        }
        wIndices.Add(wr.Fork());
      }
      wr.Write(")");
      return w;
    }

    protected override (ConcreteSyntaxTree/*array*/, ConcreteSyntaxTree/*rhs*/) EmitArrayUpdate(List<Action<ConcreteSyntaxTree>> indices, Type elementType, ConcreteSyntaxTree wr) {
      elementType = DatatypeWrapperEraser.SimplifyType(Options, elementType);
      // All arrays are dafny.ArrayN<T> wrappers: use .set(i0, i1, ..., value).
      var wArray = wr.Fork();
      wr.Write(".set(");
      for (int i = 0; i < indices.Count; i++) {
        if (i > 0) {
          wr.Write(", ");
        }
        wr.Write($"{DafnyHelpersClass}.toInt(");
        indices[i](wr);
        wr.Write(")");
      }
      wr.Write(", ");
      // The array stores the boxed element type (e.g. CodePoint), but the RHS is in the
      // native representation (e.g. Int for a char). Box it: char -> NativeObjectType.
      var wRhs = EmitCoercionIfNecessary(from: elementType, to: NativeObjectType, tok: Token.NoToken, wr: wr.Fork());
      wr.Write(")");
      return (wArray, wRhs);
    }

    protected override void EmitSeqSelectRange(Expression source, Expression lo, Expression hi, bool fromArray,
        bool inLetExprBody, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      if (fromArray) {
        wr.Write($"{DafnySeqClass}.fromRawArrayRange({TypeDescriptor(source.Type.NormalizeExpand().TypeArgs[0], wr, source.Origin)}, ");
      }
      TrParenExpr(source, wr, inLetExprBody, wStmts);
      if (fromArray) {
        wr.Write(", ");
        if (lo != null) {
          TrExprAsInt(lo, wr, inLetExprBody, wStmts);
        } else {
          wr.Write("0");
        }
        wr.Write(", ");
        if (hi != null) {
          TrExprAsInt(hi, wr, inLetExprBody, wStmts);
        } else {
          // dafny.Array1<T> wrapper exposes length as .dim0
          TrParenExpr(source, wr, inLetExprBody, wStmts);
          wr.Write(".dim0");
        }
        wr.Write(")");
      } else {
        if (lo != null && hi != null) {
          wr.Write(".subsequence(");
          TrExprAsInt(lo, wr, inLetExprBody, wStmts);
          wr.Write(", ");
          TrExprAsInt(hi, wr, inLetExprBody, wStmts);
          wr.Write(")");
        } else if (lo != null) {
          wr.Write(".drop");
          TrParenExpr(lo, wr, inLetExprBody, wStmts);
        } else if (hi != null) {
          wr.Write(".take");
          TrParenExpr(hi, wr, inLetExprBody, wStmts);
        }
      }
    }

    protected override void EmitIndexCollectionSelect(Expression source, Expression index, bool inLetExprBody,
        ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      // Taken from C# compiler, assuming source is a DafnySequence type.
      if (source.Type.NormalizeToAncestorType().AsMultiSetType is { } multiSetType) {
        wr.Write($"{DafnyMultiSetClass}.multiplicity<{BoxedTypeName(multiSetType.Arg, wr, Token.NoToken)}>(");
        TrParenExpr(source, wr, inLetExprBody, wStmts);
        wr.Write(", ");
        wr.Append(JavaCoercedExpr(index, multiSetType.Arg, inLetExprBody, wStmts));
        wr.Write(")");
      } else if (source.Type.NormalizeToAncestorType().AsMapType is { } mapType) {
        wr = EmitCoercionIfNecessary(from: NativeObjectType, to: mapType.Range, tok: source.Origin, wr: wr);
        TrParenExpr(source, wr, inLetExprBody, wStmts);
        wr.Write(".get(");
        var coercedWr = EmitCoercionIfNecessary(from: mapType.Domain, to: NativeObjectType, tok: source.Origin, wr: wr);
        EmitExpr(index, inLetExprBody, coercedWr, wStmts);
        // DafnyMap.get returns a nullable V?; Dafny's m[k] requires k in m, so the
        // value is present. Assert non-null (!!) — otherwise Kotlin rejects the
        // downstream coercion (e.g. .toInt()) on a nullable receiver.
        wr.Write(")!!");
      } else {
        wr = EmitCoercionIfNecessary(from: NativeObjectType, to: source.Type.NormalizeToAncestorType().AsCollectionType.Arg, tok: source.Origin, wr: wr);
        TrParenExpr(source, wr, inLetExprBody, wStmts);
        wr.Write(".select");
        TrParenExprAsInt(index, wr, inLetExprBody, wStmts);
      }
    }

    protected override void EmitMultiSetFormingExpr(MultiSetFormingExpr expr, bool inLetExprBody, ConcreteSyntaxTree wr,
        ConcreteSyntaxTree wStmts) {
      TrParenExpr(expr.E, wr, inLetExprBody, wStmts);
      wr.Write(".asDafnyMultiset()");
    }

    protected override void EmitIndexCollectionUpdate(Expression source, Expression index, Expression value,
        CollectionType resultCollectionType, bool inLetExprBody, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      if (resultCollectionType.AsSeqType != null) {
        wr.Write($"{DafnySeqClass}.update<{BoxedTypeName(resultCollectionType.Arg, wr, Token.NoToken)}>(");
        wr.Append(Expr(source, inLetExprBody, wStmts));
        wr.Write(", ");
        TrExprAsInt(index, wr, inLetExprBody, wStmts);
        wr.Write(", ");
        wr.Append(JavaCoercedExpr(value, resultCollectionType.ValueArg, inLetExprBody, wStmts));
        wr.Write(")");
      } else if (resultCollectionType.AsMapType is { } mapType) {
        wr.Write($"{DafnyMapClass}.update<{BoxedTypeName(mapType.Domain, wr, Token.NoToken)}, {BoxedTypeName(mapType.Range, wr, Token.NoToken)}>(");
        wr.Append(Expr(source, inLetExprBody, wStmts));
        wr.Write(", ");
        wr.Append(JavaCoercedExpr(index, mapType.Domain, inLetExprBody, wStmts));
        wr.Write(", ");
        wr.Append(JavaCoercedExpr(value, mapType.Range, inLetExprBody, wStmts));
        wr.Write(")");
      } else {
        Contract.Assert(resultCollectionType.AsMultiSetType != null);
        wr.Write($"{DafnyMultiSetClass}.update<{BoxedTypeName(resultCollectionType.Arg, wr, Token.NoToken)}>(");
        wr.Append(Expr(source, inLetExprBody, wStmts));
        wr.Write(", ");
        wr.Append(JavaCoercedExpr(index, resultCollectionType.ValueArg, inLetExprBody, wStmts));
        wr.Write(", ");
        wr.Append(Expr(value, inLetExprBody, wStmts));
        wr.Write(")");
      }
    }

    private ConcreteSyntaxTree JavaCoercedExpr(Expression expr, Type toType, bool inLetExprBody, ConcreteSyntaxTree wStmts) {
      return CoercedExpr(expr, expr.Type.IsTypeParameter ? toType : NativeObjectType, inLetExprBody, wStmts);
    }

    protected override void EmitRotate(Expression e0, Expression e1, bool isRotateLeft, ConcreteSyntaxTree wr,
      bool inLetExprBody, ConcreteSyntaxTree wStmts, FCE_Arg_Translator tr) {
      string nativeName = null;
      bool needsCast = false;
      var nativeType = AsNativeType(e0.Type);
      if (nativeType != null) {
        GetNativeInfo(nativeType.Sel, out nativeName, out _, out needsCast);
      }
      var leftShift = nativeType == null ? ".shiftLeft" : "<<";
      var rightShift = nativeType == null ? ".shiftRight" : ">>>";
      // ( e0 op1 e1) | (e0 op2 (width - e1))
      if (needsCast) {
        wr.Write("(" + nativeName + ")(" + CastIfSmallNativeType(e0.Type) + "(");
      }
      wr.Write("(");
      EmitShift(e0, e1, isRotateLeft ? leftShift : rightShift, isRotateLeft, nativeType, true, wr, inLetExprBody, wStmts, tr);
      wr.Write(")");
      if (nativeType == null) {
        wr.Write(".or");
      } else {
        wr.Write("|");
      }
      wr.Write("(");
      EmitShift(e0, e1, isRotateLeft ? rightShift : leftShift, !isRotateLeft, nativeType, false, wr, inLetExprBody, wStmts, tr);
      wr.Write(")))");
      if (needsCast) {
        wr.Write("))");
      }
    }

    private void EmitShift(Expression e0, Expression e1, string op, bool truncate, [CanBeNull] NativeType nativeType, bool firstOp,
        ConcreteSyntaxTree wr, bool inLetExprBody, ConcreteSyntaxTree wStmts, FCE_Arg_Translator tr) {
      var bv = e0.Type.NormalizeToAncestorType().AsBitVectorType;
      if (truncate) {
        wr = EmitBitvectorTruncation(bv, nativeType, true, wr);
      }
      tr(e0, wr, inLetExprBody, wStmts);
      wr.Write($" {op} ");
      if (!firstOp) {
        wr.Write($"({bv.Width} - ");
      }
      wr.Write("((");
      tr(e1, wr, inLetExprBody, wStmts);
      wr.Write(")");
      if (AsNativeType(e1.Type) == null) {
        wr.Write(".toInt()");
      }
      if (!firstOp) {
        wr.Write(")");
      }
    }

    protected override ConcreteSyntaxTree EmitBitvectorTruncation(BitvectorType bvType, [CanBeNull] NativeType nativeType,
      bool surroundByUnchecked, ConcreteSyntaxTree wr) {

      string nativeName = null, literalSuffix = null;
      if (nativeType != null) {
        GetNativeInfo(nativeType.Sel, out nativeName, out literalSuffix, out _);
      }
      // Kotlin has no C-style prefix casts and uses `and` for bitwise-and; truncation
      // to a small native type is a postfix `.toByte()/.toShort()` conversion.
      // --- Before
      wr.Write("((");
      // --- Middle
      var middle = wr.Fork();
      // --- After
      // do the truncation, if needed
      if (nativeType == null) {
        // Wrap to the bv width via `x mod 2^width` rather than `x and (2^width - 1)`.
        // The two are equal for two's-complement (java.math, used by kt/kmp-jvm), but
        // the ionspin bignum (jvm-ionspin / non-JVM KMP) is sign-magnitude and its
        // bitwise `and` on a negative operand is not two's-complement, so masking a
        // transient-negative result (e.g. `0 - 1` on bv100, or BVNot) gave the wrong
        // value. `mod` returns the non-negative residue in [0, 2^width) on both.
        wr.Write($").mod(dafny.BigInteger.ONE.shiftLeft({bvType.Width})))");
      } else if (bvType.Width < nativeType.Bitwidth) {
        // print the mask in hex, because that looks nice. Kotlin's `and` is only defined on
        // Int/Long, so for Byte/Short operands do the mask in Int space, then truncate back.
        var kotlinNative = AsKotlinNativeType(nativeType);
        if (kotlinNative is KotlinNativeType.Byte or KotlinNativeType.Short) {
          wr.Write($").toInt() and 0x{(1UL << bvType.Width) - 1:X}){NativeTruncationSuffix(nativeType)}");
        } else {
          wr.Write($") and 0x{(1UL << bvType.Width) - 1:X}{literalSuffix}){NativeTruncationSuffix(nativeType)}");
        }
      } else {
        wr.Write($")){NativeTruncationSuffix(nativeType)}");
      }
      return middle;
    }

    protected override bool CompareZeroUsingSign(Type type) {
      // Everything is boxed, so everything benefits from avoiding explicit 0
      return true;
    }

    protected override ConcreteSyntaxTree EmitSign(Type type, ConcreteSyntaxTree wr) {
      ConcreteSyntaxTree w;
      var nt = AsNativeType(type);
      if (nt == null) {
        w = wr.Fork();
        wr.Write(".signum()");  // dafny.BigInteger.signum() (JVM)
      } else if (nt.LowerBound >= 0) {
        // Unsigned native: sign is 0 or 1. Kotlin if-expression, not Java ternary.
        wr.Write("(if ((");
        w = wr.Fork();
        wr.Write(").toLong() == 0L) 0 else 1)");
      } else {
        // Signed native: use Kotlin's compareTo(0) which yields -1/0/1.
        wr.Write("((");
        w = wr.Fork();
        wr.Write(").compareTo(0))");
      }
      return w;
    }

    protected override IClassWriter/*?*/ DeclareDatatype(DatatypeDecl dt, ConcreteSyntaxTree wr) {
      if (dt is TupleTypeDecl tupleTypeDecl) {
        // CreateTuple() produces quite different code than this method would
        // by treating a tuple declaration as just a special case of a datatype.
        // Compare to the C# compiler which just compiles tuples like datatypes
        // with a bit of special handling for the name.
        // This could be changed to match at some point, but it would break
        // code that relies on the current runtime representation of tuples in Java.
        CreateTuple(tupleTypeDecl.Dims, wr);
        return null;
      }

      var w = CompileDatatypeBase(dt, wr);
      CompileDatatypeConstructors(dt, wr);
      return w;
    }

    IClassWriter CompileDatatypeBase(DatatypeDecl dt, ConcreteSyntaxTree wr) {
      var DtT_TypeArgs = TypeParameters(dt.TypeArgs);
      var justTypeArgs = dt.TypeArgs.Count == 0 ? "" : " " + DtT_TypeArgs;
      var DtT_protected = IdName(dt) + DtT_TypeArgs;
      var simplifiedType = DatatypeWrapperEraser.SimplifyType(Options, UserDefinedType.FromTopLevelDecl(dt.Origin, dt));
      var simplifiedTypeName = TypeName(simplifiedType, wr, dt.Origin);

      var filename = $"{ModulePath}/{IdName(dt)}.kt";
      wr = wr.NewFile(filename);
      wr.WriteLine($"// Class {DtT_protected}");
      wr.WriteLine($"// Dafny class {DtT_protected} compiled into Kotlin");
      wr.WriteLine($"package {ModuleName};");
      wr.WriteLine();
      //TODO: Figure out how to resolve type checking warnings
      // from here on, write everything into the new block created here:
      EmitSuppression(wr);
      wr.Write("{0}class {1}", dt.IsRecordType ? "" : "abstract ", DtT_protected);
      var superTraits = dt.ParentTypeInformation.UniqueParentTraits();
      if (superTraits.Any()) {
        wr.Write($" : {superTraits.Comma(trait => TypeName(trait, wr, dt.Origin))}");
      }
      var btw = wr.NewBlock();
      wr = btw;

      // constructor
      if (dt.IsRecordType) {
        DatatypeFieldsAndConstructor(dt.Ctors[0], 0, wr);
      } else {
        var wTypeFields = wr.Fork();
        var wCtorParams = new ConcreteSyntaxTree();
        var wCtorBody = wr.Format($"constructor({wCtorParams})").NewBlock();
        EmitTypeDescriptorsForClass(dt.TypeArgs, dt, wTypeFields, wCtorParams, null, wCtorBody);
      }

      // Static members (the type descriptor method, Default, and create_* factory
      // methods) are referenced as List.create_Cons(...) etc., so in Kotlin they must
      // live in a companion object rather than directly in the (instance) class body.
      var wInstanceBody = wr;
      wr.WriteLine("companion object {");
      var wStatic = wr.Fork();
      wr.WriteLine("}");
      wr = wStatic;

      // type descriptor needs to be initialized before default value is generated (issue 3766)
      EmitTypeDescriptorMethod(dt, dt.TypeArgs, null, null, wr);

      // default value
      var wDefaultTypeArguments = new ConcreteSyntaxTree();
      var defaultMethodTypeDescriptorCount = 0;
      var usedTypeArgs = UsedTypeParameters(dt);
      ConcreteSyntaxTree wDefault;
      ConcreteSyntaxTree wLegacyDefault = null;
      wr.WriteLine();
      // The default value for a nullable Dafny ref type is `null`, so the default field
      // and Default() return type must be nullable to match.
      var defaultTypeName = simplifiedTypeName;
      if (simplifiedType is { IsRefType: true, IsNonNullRefType: false } && !defaultTypeName.EndsWith("?")) {
        defaultTypeName += "?";
      }
      if (dt.TypeArgs.Count == 0) {
        wr.Write($"private val theDefault: {defaultTypeName} = ");
        wDefault = wr.Fork();
        wr.WriteLine("");  // No semicolon in Kotlin
        var w = wr.NewBlock($"fun Default(): {defaultTypeName}");
        w.WriteLine("return theDefault");
      } else {
        // Kotlin: fun with type parameters, no "static" keyword
        wr.Write($"fun{justTypeArgs} Default(");
        defaultMethodTypeDescriptorCount = EmitTypeDescriptorsForClass(dt.TypeArgs, dt, null, wr, wDefaultTypeArguments, null);
        var typeParameters = usedTypeArgs.Comma(tp => $"{FormatDefaultTypeParameterValue(tp)}: {tp.GetCompileName(Options)}");
        var sep = defaultMethodTypeDescriptorCount != 0 && typeParameters.Length != 0 ? ", " : "";
        wr.Write($"{sep}{typeParameters}): {defaultTypeName}");
        var w = wr.NewBlock("");
        w.Write("return ");
        wDefault = w.Fork();
        w.WriteLine("");

        if (Options.Get(KotlinBackend.LegacyDataConstructors)) {
          wr.WriteLine("@Deprecated(\"Use the version with type descriptors\")");
          w = wr.NewBlock($"fun{justTypeArgs} Default({typeParameters}): {simplifiedTypeName}");
          foreach (var typeParameter in dt.TypeArgs) {
            w.WriteLine(TypeDescriptorVariableDeclaration(typeParameter) + " = null");
          }
          w.Write("return ");
          wLegacyDefault = w.Fork();
          w.WriteLine("");
        }
      }
      var groundingCtor = dt.GetGroundingCtor();
      if (groundingCtor.IsGhost) {
        wDefault.Write(ForcePlaceboValue(simplifiedType, wDefault, dt.Origin));
      } else if (DatatypeWrapperEraser.GetInnerTypeOfErasableDatatypeWrapper(Options, dt, out var innerType)) {
        wDefault.Write(DefaultValue(innerType, wDefault, dt.Origin));
      } else {
        var nonGhostFormals = groundingCtor.Formals.Where(f => !f.IsGhost).ToList();
        var args = nonGhostFormals.Comma(f => DefaultValue(f.Type, wDefault, f.Origin));
        EmitDatatypeValue(dt, groundingCtor,
          dt.TypeArgs.ConvertAll(tp => (Type)new UserDefinedType(dt.Origin, tp)),
          dt is CoDatatypeDecl, $"{wDefaultTypeArguments}", args, wDefault);
      }

      if (wLegacyDefault != null) {
        foreach (var node in wDefault.Nodes) {
          wLegacyDefault.Append(node);
        }
      }

      // create methods - Kotlin syntax
      foreach (var ctor in dt.Ctors.Where(ctor => !ctor.IsGhost)) {
        var wCtorParams = new ConcreteSyntaxTree();
        var wCallArguments = new ConcreteSyntaxTree();
        var typeDescriptorCount = EmitTypeDescriptorsForClass(dt.TypeArgs, dt, null, wCtorParams, wCallArguments, null);
        wr.Write($"fun{justTypeArgs} {DtCreateName(ctor)}(");
        wr.Append(wCtorParams);
        var formalCount = WriteFormals(typeDescriptorCount > 0 ? ", " : "", ctor.Formals, wr);
        var sep = typeDescriptorCount > 0 && formalCount > 0 ? ", " : "";
        wr.NewBlock($"): {DtT_protected}")
          .WriteLine($"return {DtCtorDeclarationName(ctor, dt.TypeArgs)}({wCallArguments}{sep}{ctor.Formals.Where(f => !f.IsGhost).Comma(FormalName)})");

        if (dt.TypeArgs.Any() && Options.Get(KotlinBackend.LegacyDataConstructors)) {
          wr.WriteLine("@Deprecated(\"Use the version with type descriptors\")");
          wr.Write($"fun{justTypeArgs} {DtCreateName(ctor)}(");
          var nullTypeDescriptorArgs = Enumerable.Repeat("null", typeDescriptorCount).Comma();
          WriteFormals("", ctor.Formals, wr);
          wr.NewBlock($"): {DtT_protected}")
            .WriteLine($"return {DtCtorDeclarationName(ctor, dt.TypeArgs)}({nullTypeDescriptorArgs}{sep}{ctor.Formals.Where(f => !f.IsGhost).Comma(FormalName)})");
        }
      }

      if (dt.IsRecordType) {
        // Also emit a "create_<ctor_name>" method that thunks to "create",
        // to provide a more uniform interface.

        var ctor = dt.Ctors[0];
        var wCtorParams = new ConcreteSyntaxTree();
        var wCallArguments = new ConcreteSyntaxTree();
        var typeDescriptorCount = EmitTypeDescriptorsForClass(dt.TypeArgs, dt, null, wCtorParams, wCallArguments, null);
        wr.Write($"fun{justTypeArgs} create_{ctor.GetCompileName(Options)}(");
        wr.Append(wCtorParams);
        var formalCount = WriteFormals(typeDescriptorCount > 0 ? ", " : "", ctor.Formals, wr);
        var sep = typeDescriptorCount > 0 && formalCount > 0 ? ", " : "";
        wr.NewBlock($"): {DtT_protected}")
          .WriteLine($"return create({wCallArguments}{sep}{ctor.Formals.Where(f => !f.IsGhost).Comma(FormalName)})");

        if (dt.TypeArgs.Any() && Options.Get(KotlinBackend.LegacyDataConstructors)) {
          wr.WriteLine("@Deprecated(\"Use the version with type descriptors\")");
          wr.Write($"fun{justTypeArgs} create_{ctor.GetCompileName(Options)}(");
          var nullTypeDescriptorArgs = Enumerable.Repeat("null", typeDescriptorCount).Comma();
          WriteFormals("", ctor.Formals, wr);
          wr.NewBlock($"): {DtT_protected}")
            .WriteLine($"return create({nullTypeDescriptorArgs}{sep}{ctor.Formals.Where(f => !f.IsGhost).Comma(FormalName)})");
        }
      }

      // Back to the instance class body for query properties and destructors.
      wr = wInstanceBody;

      // query properties
      foreach (var ctor in dt.Ctors.Where(ctor => !ctor.IsGhost)) {
        if (dt.IsRecordType) {
          wr.WriteLine($"fun is_{ctor.GetCompileName(Options)}(): Boolean {{ return true }}");
        } else {
          wr.WriteLine($"fun is_{ctor.GetCompileName(Options)}(): Boolean {{ return this is {dt.GetCompileName(Options)}_{ctor.GetCompileName(Options)} }}");
        }
      }
      if (dt is CoDatatypeDecl) {
        wr.WriteLine($"abstract fun Get(): {DtT_protected}");
      }
      if (dt.HasFinitePossibleValues) {
        Contract.Assert(dt.TypeArgs.Count == 0);
        // Called as `Dt.AllSingletonConstructors()`, so it must live in the companion
        // object (wStatic), not the instance class body.
        var w = wStatic.NewNamedBlock($"fun AllSingletonConstructors(): MutableList<{DtT_protected}>");
        string arraylist = "singleton_iterator";
        w.WriteLine($"val {arraylist}: MutableList<{DtT_protected}> = mutableListOf()");
        foreach (var ctor in dt.Ctors) {
          Contract.Assert(ctor.Formals.Count == 0);
          if (ctor.IsGhost) {
            w.WriteLine("{0}.add({1})", arraylist, ForcePlaceboValue(UserDefinedType.FromTopLevelDecl(dt.Origin, dt), w, dt.Origin));
          } else {
            w.WriteLine("{0}.add({1}{2}())", arraylist, DtT_protected, dt.IsRecordType ? "" : $"_{ctor.GetCompileName(Options)}");
          }
        }
        w.WriteLine($"return {arraylist}");
      }
      // destructors
      foreach (var ctor in dt.Ctors) {
        foreach (var dtor in ctor.Destructors.Where(dtor => dtor.EnclosingCtors[0] == ctor)) {
          var compiledConstructorCount = dtor.EnclosingCtors.Count(constructor => !constructor.IsGhost);
          if (compiledConstructorCount == 0) {
            continue;
          }

          var arg = dtor.CorrespondingFormals[0];
          if (arg.IsGhost || !arg.HasName) {
            continue;
          }

          var wDtor = wr.NewNamedBlock($"fun dtor_{arg.GetOrCreateCompileName(currentIdGenerator)}(): {TypeName(arg.Type, wr, arg.Origin)}");
          if (dt.IsRecordType) {
            wDtor.WriteLine($"return this.{FieldName(arg, 0)}");
          } else {
            wDtor.WriteLine("val d = this{0}", dt is CoDatatypeDecl ? ".Get()" : "");
            var compiledConstructorsProcessed = 0;
            for (var i = 0; i < dtor.EnclosingCtors.Count; i++) {
              var ctor_i = dtor.EnclosingCtors[i];
              Contract.Assert(arg.GetOrCreateCompileName(currentIdGenerator) == dtor.CorrespondingFormals[i].GetOrCreateCompileName(currentIdGenerator));
              if (ctor_i.IsGhost) {
                continue;
              }
              if (compiledConstructorsProcessed < compiledConstructorCount - 1) {
                wDtor.WriteLine("if (d is {0}_{1}) {{ return (d as {0}_{1}{2}).{3} }}", dt.GetCompileName(Options),
                  ctor_i.GetCompileName(Options), DtT_TypeArgs, FieldName(arg, i));
              } else {
                wDtor.WriteLine($"return (d as {dt.GetCompileName(Options)}_{ctor_i.GetCompileName(Options)}{DtT_TypeArgs}).{FieldName(arg, 0)}");
              }
              compiledConstructorsProcessed++;
            }
          }
        }
      }

      // FIXME: This is dodgy.  We can set the constructor body writer to null
      // only because we don't expect to use it, which is only because we don't
      // expect there to be fields.
      // Static members of the datatype (e.g. static functions/methods declared on a
      // generic datatype) are called as `Dt.M(...)`, so they must live in the
      // companion object (wStatic), not the instance class body (btw).
      return new ClassWriter(this, btw, ctorBodyWriter: null, staticMemberWriter: wStatic);
    }

    void CompileDatatypeConstructors(DatatypeDecl dt, ConcreteSyntaxTree wrx) {
      Contract.Requires(dt != null);
      string typeParams = TypeParameters(dt.TypeArgs);
      if (dt.IsRecordType) {
        // There is only one constructor, and it is populated by CompileDatatypeBase
        return;
      }
      int constructorIndex = 0; // used to give each constructor a different name
      foreach (DatatypeCtor ctor in dt.Ctors.Where(ctor => !ctor.IsGhost)) {
        var filename = $"{ModulePath}/{DtCtorDeclarationName(ctor)}.kt";
        var wr = wrx.NewFile(filename);
        wr.WriteLine($"// Class {DtCtorDeclarationName(ctor, dt.TypeArgs)}");
        wr.WriteLine($"// Dafny class {DtCtorDeclarationName(ctor, dt.TypeArgs)} compiled into Kotlin");
        wr.WriteLine($"package {ModuleName};");
        wr.WriteLine();
        EmitSuppression(wr);
        var w = wr.NewNamedBlock($"class {DtCtorDeclarationName(ctor, dt.TypeArgs)} : {IdName(dt)}{typeParams}");
        DatatypeFieldsAndConstructor(ctor, constructorIndex, w);
        constructorIndex++;
      }
      if (dt is CoDatatypeDecl) {
        var filename = $"{ModulePath}/{dt.GetCompileName(Options)}__Lazy.kt";
        var wr = wrx.NewFile(filename);
        wr.WriteLine($"// Class {dt.GetCompileName(Options)}__Lazy");
        wr.WriteLine($"// Dafny class {dt.GetCompileName(Options)}__Lazy compiled into Kotlin");
        wr.WriteLine($"package {ModuleName};");
        wr.WriteLine();
        EmitSuppression(wr); //TODO: Fix implementations so they do not need this suppression
        var w = wr.NewNamedBlock($"class {dt.GetCompileName(Options)}__Lazy{typeParams} : {IdName(dt)}{typeParams}");
        // `fun interface` (Kotlin SAM) so a plain `{ ... }` lambda thunk converts to
        // Computer at the call site. The return type needs the datatype's type args.
        w.WriteLine($"fun interface Computer{typeParams} {{ fun run(): {dt.GetCompileName(Options)}{typeParams} }}");
        // Kotlin requires initialization; these are set in the constructor.
        w.WriteLine($"var c: Computer{typeParams}? = null");
        w.WriteLine($"var d: {dt.GetCompileName(Options)}{typeParams}? = null");

        var wCtorParams = new ConcreteSyntaxTree();
        var wBaseCallArguments = new ConcreteSyntaxTree();
        var typeDescriptorCount = EmitTypeDescriptorsForClass(dt.TypeArgs, dt, null, wCtorParams, wBaseCallArguments, null);
        var sep = typeDescriptorCount > 0 ? ", " : "";
        // Kotlin calls the base constructor in the header (`: super(...)`), not the body.
        var wCtorBody = w.NewBlock($"constructor({wCtorParams}{sep}c: Computer{typeParams}) : super({wBaseCallArguments})");
        wCtorBody.WriteLine("this.c = c");
        w.WriteLine($"override fun Get(): {dt.GetCompileName(Options)}{typeParams} {{ if (c != null) {{ d = c!!.run(); c = null }}; return d!! }}");
        w.WriteLine("override fun toString(): String { return Get().toString() }");
      }
    }

    void DatatypeFieldsAndConstructor(DatatypeCtor ctor, int constructorIndex, ConcreteSyntaxTree wr) {
      Contract.Requires(ctor != null);
      Contract.Requires(0 <= constructorIndex && constructorIndex < ctor.EnclosingDatatype.Ctors.Count);
      Contract.Requires(wr != null);
      var dt = ctor.EnclosingDatatype;
      var i = 0;
      foreach (Formal arg in ctor.Formals) {
        if (!arg.IsGhost) {
          var fieldTypeName = TypeName(arg.Type, wr, arg.Origin);
          // Nullable Dafny ref types need `?` (default value uses `null as T?`).
          if (arg.Type is { IsRefType: true, IsNonNullRefType: false } && !fieldTypeName.EndsWith("?")) {
            fieldTypeName += "?";
          }
          wr.WriteLine($"val {FieldName(arg, i)}: {fieldTypeName}");
          i++;
        }
      }

      var wTypeFields = wr.Fork();
      var wCtorParams = new ConcreteSyntaxTree();
      int typeDescriptorCount;
      ConcreteSyntaxTree wCtorBody;
      if (ctor.EnclosingDatatype.IsRecordType) {
        wCtorBody = wr.Format($"constructor({wCtorParams})").NewBlock();
        typeDescriptorCount = EmitTypeDescriptorsForClass(dt.TypeArgs, dt, wTypeFields, wCtorParams, null, wCtorBody);
      } else {
        // Kotlin: super constructor is called in the delegation position:
        //   constructor(params) : super(args) { body }
        var wBaseCallArguments = new ConcreteSyntaxTree();
        typeDescriptorCount = EmitTypeDescriptorsForClass(dt.TypeArgs, dt, null, wCtorParams, wBaseCallArguments, null);
        wCtorBody = wr.Format($"constructor({wCtorParams}) : super({wBaseCallArguments})").NewBlock();
      }
      WriteFormals(typeDescriptorCount > 0 ? ", " : "", ctor.Formals, wCtorParams);
      {
        var w = wCtorBody;
        i = 0;
        foreach (Formal arg in ctor.Formals) {
          if (!arg.IsGhost) {
            w.WriteLine($"this.{FieldName(arg, i)} = {FormalName(arg, i)};");
            i++;
          }
        }
      }
      if (dt is CoDatatypeDecl) {
        string typeParams = TypeParameters(dt.TypeArgs);
        // Concrete constructor's Get() overrides the abstract Get() on the codatatype;
        // emit Kotlin `override fun Get(): T { return this }`, not Java syntax.
        wr.WriteLine($"override fun Get(): {dt.GetCompileName(Options)}{typeParams} {{ return this }}");
      }
      // Equals method - Kotlin override
      wr.WriteLine();
      {
        var w = wr.NewBlock("override fun equals(other: Any?): Boolean");
        w.WriteLine("if (this === other) return true");
        w.WriteLine("if (other == null) return false");
        // Kotlin-native, multiplatform runtime class check (JVM `.javaClass` is
        // JVM-only and does not exist on JS/Native).
        w.WriteLine("if (this::class != other::class) return false");
        string typeParams = TypeParameters(dt.TypeArgs);
        w.WriteLine("val o = other as {0}", DtCtorDeclarationName(ctor, dt.TypeArgs));
        w.Write("return true");
        i = 0;
        foreach (var arg in ctor.Formals) {
          if (!arg.IsGhost) {
            var nm = FieldName(arg, i);
            w.Write(" && ");
            if (IsDirectlyComparable(DatatypeWrapperEraser.SimplifyType(Options, arg.Type))) {
              w.Write($"this.{nm} == o.{nm}");
            } else {
              w.Write($"this.{nm} == o.{nm}");  // Kotlin uses == for structural equality
            }
            i++;
          }
        }
        w.WriteLine("");
      }
      // HashCode method (Uses the djb2 algorithm) - Kotlin override
      {
        var w = wr.NewBlock("override fun hashCode(): Int");
        w.WriteLine("var hash = 5381L");
        w.WriteLine($"hash = ((hash shl 5) + hash) + {constructorIndex}");
        i = 0;
        foreach (Formal arg in ctor.Formals) {
          if (!arg.IsGhost) {
            string nm = FieldName(arg, i);
            w.WriteLine($"hash = ((hash shl 5) + hash) + (this.{nm}?.hashCode() ?: 0)");
            i++;
          }
        }
        w.WriteLine("return hash.toInt()");
      }

      wr.WriteLine();
      {
        var w = wr.NewBlock("override fun toString(): String");
        string nm;
        if (dt is TupleTypeDecl) {
          nm = "";
        } else {
          nm = (dt.EnclosingModuleDefinition.TryToAvoidName ? "" : dt.EnclosingModuleDefinition.Name + ".") + dt.Name + "." + ctor.Name;
        }
        if (dt is TupleTypeDecl && ctor.Formals.Count == 0) {
          // here we want parentheses and no name
          w.WriteLine("return \"()\";");
        } else if (dt is CoDatatypeDecl) {
          w.WriteLine($"return \"{nm}\";");
        } else {
          var tempVar = GenVarName("s", ctor.Formals);
          w.WriteLine($"val {tempVar} = StringBuilder()");
          w.WriteLine($"{tempVar}.append(\"{nm}\");");
          if (ctor.Formals.Count != 0) {
            w.WriteLine($"{tempVar}.append(\"(\");");
            i = 0;
            foreach (var arg in ctor.Formals) {
              if (!arg.IsGhost) {
                if (i != 0) {
                  w.WriteLine($"{tempVar}.append(\", \");");
                }
                w.Write($"{tempVar}.append(");
                var memberName = FieldName(arg, i);
                if (UnicodeCharEnabled && arg.Type.IsCharType) {
                  w.Write($"{DafnyHelpersClass}.ToCharLiteral(this.{memberName})");
                } else if (UnicodeCharEnabled && arg.Type.IsStringType) {
                  w.Write($"{DafnyHelpersClass}.ToStringLiteral(this.{memberName})");
                } else if (IsJavaPrimitiveType(arg.Type)) {
                  w.Write($"this.{memberName}");
                } else {
                  w.Write($"{DafnyHelpersClass}.toString(this.{memberName})");
                }
                w.WriteLine(");");
                i++;
              }
            }
            w.WriteLine($"{tempVar}.append(\")\");");
          }
          w.WriteLine($"return {tempVar}.toString();");
        }
      }
    }

    string DtCtorDeclarationName(DatatypeCtor ctor, List<TypeParameter>/*?*/ typeParams) {
      Contract.Requires(ctor != null);
      Contract.Ensures(Contract.Result<string>() != null);

      return DtCtorDeclarationName(ctor) + TypeParameters(typeParams);
    }
    string DtCtorDeclarationName(DatatypeCtor ctor) {
      Contract.Requires(ctor != null);
      Contract.Ensures(Contract.Result<string>() != null);

      var dt = ctor.EnclosingDatatype;
      return dt.IsRecordType ? IdName(dt) : dt.GetCompileName(Options) + "_" + ctor.GetCompileName(Options);
    }
    string DtCtorName(DatatypeCtor ctor, List<Type> typeArgs, ConcreteSyntaxTree wr) {
      Contract.Requires(ctor != null);
      Contract.Ensures(Contract.Result<string>() != null);

      var s = DtCtorName(ctor);
      if (typeArgs != null && typeArgs.Count != 0) {
        s += "<" + BoxedTypeNames(typeArgs, wr, ctor.Origin) + ">";
      }
      return s;
    }
    string DtCtorName(DatatypeCtor ctor) {
      Contract.Requires(ctor != null);
      Contract.Ensures(Contract.Result<string>() != null);

      var dt = ctor.EnclosingDatatype;
      if (dt is TupleTypeDecl tupleDecl) {
        return DafnyTupleClass(tupleDecl.NonGhostDims);
      }
      var dtName = IdProtectModule(dt.EnclosingModuleDefinition.GetCompileName(Options)) + "." + IdName(dt);
      return dt.IsRecordType ? dtName : dtName + "_" + ctor.GetCompileName(Options);
    }
    string DtCreateName(DatatypeCtor ctor) {
      Contract.Assert(!ctor.IsGhost); // there should never be an occasion to ask for a ghost constructor
      if (ctor.EnclosingDatatype.IsRecordType) {
        return "create";
      }
      return "create_" + ctor.GetCompileName(Options);
    }

    private string FieldName(Formal formal, int i) {
      Contract.Requires(formal != null);
      Contract.Ensures(Contract.Result<string>() != null);

      return IdProtect(InternalFieldPrefix + (formal.HasName ? formal.CompileName : "a" + i));
    }

    protected override void EmitPrintStmt(ConcreteSyntaxTree wr, Expression arg) {
      var wStmts = wr.Fork();
      wr.Write("print(");
      EmitToString(wr, arg, wStmts);
      wr.WriteLine(");");
    }

    protected void EmitToString(ConcreteSyntaxTree wr, Expression arg, ConcreteSyntaxTree wStmts) {
      if (arg.Type.IsArrowType) {
        var expr = arg.Resolved;
        if (expr is IdentifierExpr id) {
          // Kotlin if-expression, not Java ternary.
          wr.Write($"if ({IdName(id.Var)} == null) null else \"Function\"");
        } else {
          wr.Write("\"Function\"");
        }
      } else {
        var argumentWriter = EmitToString(wr, arg.Type);
        argumentWriter.Append(Expr(arg, false, wStmts));
      }
    }

    private ConcreteSyntaxTree EmitToString(ConcreteSyntaxTree wr, Type type) {
      Contract.Requires(!type.IsArrayType);
      ConcreteSyntaxTree argumentWriter;
      type = DatatypeWrapperEraser.SimplifyTypeAndTrimNewtypes(Options, type);
      if (AsNativeType(type) != null && AsNativeType(type).LowerBound >= 0) {
        var nativeName = GetNativeTypeName(AsNativeType(type));
        switch (AsNativeType(type).Sel) {
          case NativeType.Selection.Byte:
            wr.Write("(");
            argumentWriter = wr.Fork();
            wr.Write(").toUByte().toString()");
            break;
          case NativeType.Selection.UShort:
            wr.Write("(");
            argumentWriter = wr.Fork();
            wr.Write(").toUShort().toString()");
            break;
          case NativeType.Selection.UInt:
            wr.Write("(");
            argumentWriter = wr.Fork();
            wr.Write(").toUInt().toString()");
            break;
          case NativeType.Selection.ULong:
            wr.Write("(");
            argumentWriter = wr.Fork();
            wr.Write(").toULong().toString()");
            break;
          default:
            // Should be an unsigned type by assumption
            Contract.Assert(false);
            throw new Cce.UnreachableException();
        }
      } else {
        bool isGeneric = type.NormalizeToAncestorType().AsSeqType is { Arg: { IsTypeParameter: true } };
        if (type.NormalizeToAncestorType().IsStringType) {
          argumentWriter = wr.ForkInParens();
          wr.Write(".verbatimString()");
        } else if (type.NormalizeToAncestorType().IsCharType && UnicodeCharEnabled) {
          wr.Write($"{DafnyHelpersClass}.ToCharLiteral(");
          argumentWriter = wr.Fork();
          wr.Write(")");
        } else if (isGeneric && !UnicodeCharEnabled) {
          // Multiplatform runtime check: is this a seq<char> (print as a string) or
          // some other generic seq? Use `::class` (JVM `.java`/`Character` do not
          // exist on JS/Native).
          wr.Write($"{{ _s: {DafnySeqClass}<*> -> if (_s.elementType().defaultValue()::class == Char::class) _s.verbatimString() else _s.toString() }}(");
          argumentWriter = wr.Fork();
          wr.Write(")");
        } else {
          wr.Write("(");
          argumentWriter = wr.Fork();
          wr.Write(").toString()");
        }
      }
      return argumentWriter;
    }

    protected override string IdProtect(string name) {
      return PublicIdProtectAux(name);
    }

    public override string PublicIdProtect(string name) {
      return PublicIdProtectAux(name);
    }

    private static string PublicIdProtectAux(string name) {
      name = name.Replace("_module", "_System");
      if (name == "" || name.First() == '_') {
        return name; // no need to further protect this name
      }

      // TODO: Finish with all the public IDs that need to be protected
      switch (name) {
        // keywords Java 8 and before
        // https://docs.oracle.com/javase/tutorial/java/nutsandbolts/_keywords.html
        case "abstract":
        case "assert":
        case "break":
        case "byte":
        case "case":
        case "catch":
        case "char":
        case "class":
        case "continue":
        case "default":
        case "do":
        case "double":
        case "else":
        case "enum":
        case "extends":
        case "final":
        case "finally":
        case "float":
        case "for":
        case "if":
        case "implements":
        case "import":
        case "instanceof":
        case "int":
        case "interface":
        case "long":
        case "native":
        case "new":
        case "package":
        case "private":
        case "public":
        case "return":
        case "short":
        case "static":
        case "strictfp":
        case "super":
        case "switch":
        case "synchronized":
        case "this":
        case "throw":
        case "throws":
        case "transient":
        case "try":
        case "void":
        case "volatile":
        case "while":
        // keywords since Java 9
        case "exports":
        case "module":
        case "requires":
        // no longer used in Java but still reserved as keywords
        case "const":
        case "goto":
        // special identifiers since Java 10
        case "var":
        // literal values
        case "false":
        case "null":
        case "true":
        // Kotlin hard keywords not in the Java set above (`class`, `super`, `this`,
        // `return`, `while`, `for`, `if`, `else`, `do`, `null`, `true`, `false`,
        // `package`, `interface`, `throw` are already covered).
        case "val":
        case "fun":
        case "object":
        case "is":
        case "in":
        case "as":
        case "when":
        case "typealias":
        case "typeof":
        case "toString":
        case "equals":
        case "hashCode":
        case "Default":
          return name + "_"; // TODO: figure out what to do here (C# uses @, Go uses _, JS uses _$$_)
        default:
          return name; // Package name is not a keyword, so it can be used
      }
    }

    protected override void EmitReturn(List<Formal> outParams, ConcreteSyntaxTree wr) {
      outParams = outParams.Where(f => !f.IsGhost).ToList();
      if (outParams.Count == 0) {
        wr.WriteLine("return;");
      } else if (outParams.Count == 1) {
        wr.WriteLine($"return {IdName(outParams[0])};");
      } else {
        wr.WriteLine($"return {DafnyTupleClass(outParams.Count)}({Util.Comma(outParams, IdName)});");
      }
    }

    // TODO: See if more types need to be added
    bool IsDirectlyComparable(Type t) {
      Contract.Requires(t != null);
      return t.IsBoolType || t.IsCharType || t.IsRefType || AsKotlinNativeType(t) != null;
    }

    protected override void EmitActualTypeArgs(List<Type> typeArgs, IOrigin tok, ConcreteSyntaxTree wr) {
      if (typeArgs.Count != 0) {
        wr.Write("<" + BoxedTypeNames(typeArgs, wr, tok) + ">");
      }
    }

    protected override void EmitITE(Expression guard, Expression thn, Expression els, Type resultType, bool inLetExprBody,
        ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      // Kotlin uses an if/else expression instead of the Java ternary operator.
      resultType = resultType.NormalizeExpand();
      var thenExpr = Expr(thn, inLetExprBody, wStmts);
      var castedThenExpr = resultType.Equals(thn.Type.NormalizeExpand()) ? thenExpr : Cast(resultType, thenExpr);
      var elseExpr = Expr(els, inLetExprBody, wStmts);
      var castedElseExpr = resultType.Equals(els.Type.NormalizeExpand()) ? elseExpr : Cast(resultType, elseExpr);
      wr.Format($"(if (({Expr(guard, inLetExprBody, wStmts)})) ({castedThenExpr}) else ({castedElseExpr}))");
    }

    protected override void EmitNameAndActualTypeArgs(string protectedName, List<Type> typeArgs, IOrigin tok,
      Expression customReceiver, bool receiverAsArgument, ConcreteSyntaxTree wr) {
      // Kotlin places explicit type arguments AFTER the method name: name<T>(...)
      wr.Write(protectedName);
      EmitActualTypeArgs(typeArgs, tok, wr);
    }

    protected override void EmitNew(Type type, IOrigin tok, CallStmt initCall, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      var ctor = (Constructor)initCall?.Method; // correctness of cast follows from precondition of "EmitNew"
      wr.Write($"{TypeName(type, wr, tok)}(");
      var sep = "";
      if (type is UserDefinedType definedType) {
        var typeArguments = TypeArgumentInstantiation.ListFromClass(definedType.ResolvedClass, definedType.TypeArgs);
        EmitTypeDescriptorsActuals(typeArguments, tok, wr, ref sep);
      }
      wr.Write(ConstructorArguments(initCall, wStmts, ctor, sep));
      wr.Write(")");
    }

    /// <summary>
    /// Returns whether or not there is a run-time type descriptor corresponding to "tp".
    ///
    /// Note, one might think that this method should return "tp.Characteristics.HasCompiledValue".
    /// However, currently, all built-in collection types in Java use type descriptors for their arguments.
    /// To get this threaded through everywhere, all type arguments must always be passed with a
    /// corresponding type descriptor. :(  Thus, this method returns "true".
    /// </summary>
    protected override bool NeedsTypeDescriptor(TypeParameter tp) {
      return tp.Parent is not TupleTypeDecl;
    }

    protected override void TypeArgDescriptorUse(bool isStatic, bool lookasideBody, TopLevelDeclWithMembers cl, out bool needsTypeParameter, out bool needsTypeDescriptor) {
      if (cl is DatatypeDecl dt) {
        needsTypeParameter = isStatic || DatatypeWrapperEraser.IsErasableDatatypeWrapper(Options, dt, out _);
        needsTypeDescriptor = true;
      } else if (cl is TraitDecl) {
        needsTypeParameter = isStatic || lookasideBody;
        needsTypeDescriptor = isStatic || lookasideBody;
      } else {
        Contract.Assert(cl is ClassDecl);
        needsTypeParameter = isStatic;
        needsTypeDescriptor = isStatic;
      }
    }

    protected override string TypeDescriptor(Type type, ConcreteSyntaxTree wr, IOrigin tok) {
      type = DatatypeWrapperEraser.SimplifyTypeAndTrimSubsetTypes(Options, type);
      if (type is BoolType) {
        return $"{DafnyTypeDescriptor}.BOOLEAN";
      } else if (type is CharType) {
        return UnicodeCharEnabled ? $"{DafnyTypeDescriptor}.UNICODE_CHAR" : $"{DafnyTypeDescriptor}.CHAR";
      } else if (type is IntType or BigOrdinalType) {
        return $"{DafnyTypeDescriptor}.BIG_INTEGER";
      } else if (type is RealType) {
        return $"{DafnyTypeDescriptor}.BIG_RATIONAL";
      } else if (type is BitvectorType) {
        var t = (BitvectorType)type;
        if (t.NativeType != null) {
          return GetNativeTypeDescriptor(AsNativeType(type));
        } else {
          return $"{DafnyTypeDescriptor}.BIG_INTEGER";
        }
      } else if (type is SetType setType) {
        return CoerceTypeDescriptor(AddTypeDescriptorArgs(DafnySetClass, setType.TypeArgs, setType.TypeArgs, wr, tok), type, wr, tok);
      } else if (type is SeqType seqType) {
        return CoerceTypeDescriptor(AddTypeDescriptorArgs(DafnySeqClass, seqType.TypeArgs, seqType.TypeArgs, wr, tok), type, wr, tok);
      } else if (type is MultiSetType multiSetType) {
        return CoerceTypeDescriptor(AddTypeDescriptorArgs(DafnyMultiSetClass, multiSetType.TypeArgs, multiSetType.TypeArgs, wr, tok), type, wr, tok);
      } else if (type is MapType mapType) {
        return CoerceTypeDescriptor(AddTypeDescriptorArgs(DafnyMapClass, mapType.TypeArgs, mapType.TypeArgs, wr, tok), type, wr, tok);
      } else if (type.IsArrayType) {
        ArrayClassDecl at = type.AsArrayType;
        var elType = UserDefinedType.ArrayElementType(type);
        var elTypeName = TypeName(elType, wr, tok, true);
        if (at.Dims > 1) {
          return $"{DafnyMultiArrayClass(at.Dims)}.{TypeMethodName}<{elTypeName}>()";
        } else if (elType.IsBoolType || elType.IsCharType || AsNativeType(elType) != null) {
          // The native-array descriptors (BYTE_ARRAY etc.) are typed on the JVM
          // primitive array (TypeDescriptor<ByteArray>), but the expected type is
          // TypeDescriptor<Array1<Byte>>. Cast to the boxed Dafny array type, as the
          // generic branch below does via arrayType().
          string nativeDescriptor;
          if (elType.IsBoolType) {
            nativeDescriptor = $"{DafnyTypeDescriptor}.BOOLEAN_ARRAY";
          } else if (elType.IsCharType) {
            nativeDescriptor = $"{DafnyTypeDescriptor}.CHAR_ARRAY";
          } else {
            nativeDescriptor = AsKotlinNativeType(elType) switch {
              KotlinNativeType.Byte => $"{DafnyTypeDescriptor}.BYTE_ARRAY",
              KotlinNativeType.Short => $"{DafnyTypeDescriptor}.SHORT_ARRAY",
              KotlinNativeType.Int => $"{DafnyTypeDescriptor}.INT_ARRAY",
              KotlinNativeType.Long => $"{DafnyTypeDescriptor}.LONG_ARRAY",
              _ => throw new Cce.UnreachableException(),
            };
          }
          return $"({nativeDescriptor} as {DafnyTypeDescriptor}<{BoxedTypeName(type, wr, tok)}>)";
        } else {
          // Kotlin postfix cast: ((elemTd).arrayType() as TypeDescriptor<T>)
          return $"(({TypeDescriptor(elType, wr, tok)}).arrayType() as {DafnyTypeDescriptor}<{BoxedTypeName(type, wr, tok)}>)";
        }
      } else if (type.IsObjectQ || type.IsObject) {
        return $"{DafnyTypeDescriptor}.OBJECT";
      } else if (type.IsRefType || type.IsTraitType) {
        var typeName = TypeName(type, wr, tok);
        var typeNameSansTypeParameters = StripTypeParameters(typeName);
        // Reflection-free reference descriptor: pass an `is` predicate instead of a Class token.
        return $"({DafnyTypeDescriptor}.reference<{typeName}> {{ it is {typeNameSansTypeParameters} }} " +
               $"as {DafnyTypeDescriptor}<{typeName}>)";
      } else if (type.IsTypeParameter) {
        var tp = type.AsTypeParameter;
        Contract.Assert(tp != null);
        if (thisContext != null && thisContext.ParentFormalTypeParametersToActuals.TryGetValue(tp, out var instantiatedTypeParameter)) {
          return TypeDescriptor(instantiatedTypeParameter, wr, tok);
        }
        return FormatTypeDescriptorVariable(type.AsTypeParameter.GetCompileName(Options));
      } else if (type.IsBuiltinArrowType) {
        // Arrow types are emitted as Kotlin function types `(A) -> R` (= kotlin.FunctionN),
        // so their descriptor must be generic in T (inferred at the call site) rather than
        // the fixed dafny.FunctionN._typeDescriptor(...), which yields TypeDescriptor<dafny.
        // FunctionN> and mismatches the kotlin.FunctionN target. The reflection-based
        // TypeDescriptor.function(...) returns TypeDescriptor<T> and works for any arity.
        var arrowType = type.AsArrowType;
        var argDescriptor = arrowType.Arity == 0
          ? $"{DafnyTypeDescriptor}.OBJECT"
          : TypeDescriptor(arrowType.Args[0], wr, tok);
        return $"{DafnyTypeDescriptor}.function({argDescriptor}, {TypeDescriptor(arrowType.Result, wr, tok)})";
      } else if (type is UserDefinedType udt) {
        var s = FullTypeName(udt, null, true);
        var cl = udt.ResolvedClass;
        Contract.Assert(cl != null);

        if (cl.IsExtern(Options, out _, out _)) {
          var td = $"{DafnyTypeDescriptor}.<{BoxedTypeName(type, wr, tok)}> findType({s}.class";
          if (udt.TypeArgs != null && udt.TypeArgs.Count > 0) {
            td += $", {Util.Comma(udt.TypeArgs, arg => TypeDescriptor(arg, wr, tok))}";
          }
          return td + ")";
        }

        List<Type> relevantTypeArgs;
        if (type.IsBuiltinArrowType) {
          relevantTypeArgs = type.TypeArgs;
        } else if (cl is DatatypeDecl dt) {
          relevantTypeArgs = udt.TypeArgs;
        } else {
          relevantTypeArgs = [];
          for (int i = 0; i < cl.TypeArgs.Count; i++) {
            if (NeedsTypeDescriptor(cl.TypeArgs[i])) {
              relevantTypeArgs.Add(udt.TypeArgs[i]);
            }
          }
        }

        return AddTypeDescriptorArgs(s, udt.TypeArgs, relevantTypeArgs, wr, udt.Origin);
      } else {
        Contract.Assert(false); throw new Cce.UnreachableException();
      }
    }

    private string GetNativeTypeDescriptor(NativeType nt) {
      switch (AsKotlinNativeType(nt)) {
        case KotlinNativeType.Byte: return $"{DafnyTypeDescriptor}.BYTE";
        case KotlinNativeType.Short: return $"{DafnyTypeDescriptor}.SHORT";
        case KotlinNativeType.Int: return $"{DafnyTypeDescriptor}.INT";
        case KotlinNativeType.Long: return $"{DafnyTypeDescriptor}.LONG";
        default: Contract.Assert(false); throw new Cce.UnreachableException();
      }
    }

    // The runtime collection _typeDescriptor methods (DafnySequence/DafnySet/...) return a
    // covariantly-projected descriptor, e.g. TypeDescriptor<DafnySequence<out T>>. Consumers
    // (generated classes/methods) declare their type-descriptor parameters with the invariant
    // element type, TypeDescriptor<DafnySequence<T>>. Cast the runtime value to the invariant
    // boxed type so the two are assignment-compatible.
    private string CoerceTypeDescriptor(string descriptorExpr, Type type, ConcreteSyntaxTree wr, IOrigin tok) {
      return $"({descriptorExpr} as {DafnyTypeDescriptor}<{BoxedTypeName(type, wr, tok)}>)";
    }

    private string AddTypeDescriptorArgs(string fullCompileName, List<Type> typeArgs, List<Type> relevantTypeArgs, ConcreteSyntaxTree wr, IOrigin tok) {
      Contract.Requires(fullCompileName != null);
      Contract.Requires(typeArgs != null);
      Contract.Requires(relevantTypeArgs != null);
      Contract.Requires(wr != null);
      Contract.Requires(tok != null);

      string s = $"{IdProtect(fullCompileName)}.{TypeMethodName}";
      if (typeArgs != null && typeArgs.Count != 0) {
        s += $"<{BoxedTypeNames(typeArgs, wr, tok)}>";
      }
      s += "(";
      s += Util.Comma(relevantTypeArgs, arg => TypeDescriptor(arg, wr, tok));
      return s + ")";
    }

    protected override void EmitSetBuilder_New(ConcreteSyntaxTree wr, SetComprehension e, string collectionName) {
      wr.WriteLine($"val {collectionName}: MutableList<{BoxedTypeName(e.Type.NormalizeToAncestorType().AsSetType.Arg, wr, e.Origin)}> = mutableListOf()");
    }

    protected override void EmitMapBuilder_New(ConcreteSyntaxTree wr, MapComprehension e, string collectionName) {
      var mt = e.Type.NormalizeToAncestorType().AsMapType;
      var domType = mt.Domain;
      var ranType = mt.Range;
      // Kotlin: `val name = HashMap<K, V>()` (not Java `HashMap<K,V> name = ...`).
      wr.WriteLine($"val {collectionName} = HashMap<{BoxedTypeName(domType, wr, e.Origin)}, {BoxedTypeName(ranType, wr, e.Origin)}>()");
    }

    protected override void OrganizeModules(Program program, out List<ModuleDefinition> modules) {
      modules = [];
      foreach (var m in program.CompileModules) {
        if (!m.IsDefaultModule && !m.Name.Equals("_System")) {
          modules.Add(m);
        }
      }
      foreach (var m in program.CompileModules) {
        if (m.Name.Equals("_System")) {
          modules.Add(m);
        }
      }
      foreach (var m in program.CompileModules) {
        if (m.IsDefaultModule) {
          modules.Add(m);
        }
      }
    }

    protected override bool AllowMixingImportsAndNonImports => false;

    protected override void EmitDatatypeValue(DatatypeValue dtv, string typeDescriptorArguments, string arguments, ConcreteSyntaxTree wr) {
      var dt = dtv.Ctor.EnclosingDatatype;
      var typeArgs = SelectNonGhost(dt, dtv.InferredTypeArgs);
      EmitDatatypeValue(dt, dtv.Ctor, typeArgs, dtv.IsCoCall, typeDescriptorArguments, arguments, wr);
    }

    void EmitDatatypeValue(DatatypeDecl dt, DatatypeCtor ctor, List<Type> typeArgs, bool isCoCall,
      string typeDescriptorArguments, string arguments, ConcreteSyntaxTree wr) {
      var modname = IdProtectModule(dt.EnclosingModuleDefinition.GetCompileName(Options));
      modname = (modname == ModuleName ? "" : modname + ".");
      var dtName = dt is TupleTypeDecl tupleDecl
        ? DafnyTupleClass(tupleDecl.NonGhostDims)
        : modname + IdName(dt);
      var typeParams = typeArgs.Count == 0 ? "" : $"<{BoxedTypeNames(typeArgs, wr, dt.Origin)}>";
      var sep = typeDescriptorArguments.Length != 0 && arguments.Length != 0 ? ", " : "";
      if (!isCoCall) {
        // For an ordinary constructor (that is, one that does not guard any co-recursive calls), generate:
        //   Dt.<T>create_Cons( args )
        wr.Write($"{dtName}.{DtCreateName(ctor)}{typeParams}({typeDescriptorArguments}{sep}{arguments})");
      } else {
        var sep0 = typeDescriptorArguments.Length != 0 ? ", " : "";

        wr.Write($"{modname}{IdName(dt)}__Lazy({typeDescriptorArguments}{sep0}");
        // Kotlin lambda for the lazy/co-recursive thunk: `{ Dt.create_Cons(args) }`
        // (Java emitted `() -> { return ...; }`, which Kotlin can't parse here).
        wr.Write("{ ");
        wr.Write($"{DtCtorName(ctor)}{typeParams}({typeDescriptorArguments}{sep}{arguments})");
        wr.Write(" })");
      }
    }

    protected override ConcreteSyntaxTree CreateLambda(List<Type> inTypes, IOrigin tok, List<string> inNames,
        Type resultType, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts, bool untyped = false) {
      // Emit an inline Kotlin anonymous function expression:  fun(p: T, ...): R { ... }
      // Anonymous functions (unlike hoisted local "fun NAME" + "::NAME" references) close
      // over enclosing locals, which is required for nested closures that capture variables.
      // They also support normal block bodies with explicit "return", which the base class
      // emits via EmitReturnExpr. The value is assignable to a Kotlin function type (A) -> B.
      var paramList = inNames.Zip(inTypes,
        (name, type) => $"{name}: {BoxedTypeName(type, wr, tok)}"
      ).Comma();

      var wrBody = wr.NewBlock($"fun({paramList}): {BoxedTypeName(resultType, wr, tok)}");

      return wrBody;
    }

    protected override ConcreteSyntaxTree CreateIIFE0(Type resultType, IOrigin resultTok, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      // Kotlin lambdas forbid a bare `return`, but the body the base class emits uses
      // `return` (via EmitReturnExpr). An anonymous function allows `return`, so use an
      // immediately-invoked anonymous function: (fun(): T { ...; return x })()
      wr.Write($"(fun(): {BoxedTypeName(resultType, wr, resultTok)} ");
      // footer is appended AFTER the block's closing brace, so it must not repeat `}`.
      var wrBody = wr.NewBlock("", ")()");
      return wrBody;
    }

    protected override void EmitUnaryExpr(ResolvedUnaryOp op, Expression expr, bool inLetExprBody, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      switch (op) {
        case ResolvedUnaryOp.BoolNot:
          TrParenExpr("!", expr, wr, inLetExprBody, wStmts);
          break;
        case ResolvedUnaryOp.BitwiseNot:
          if (AsNativeType(expr.Type) != null) {
            // Kotlin has no `~` operator; bitwise-not is the `inv()` method, and Byte
            // and Short don't even have inv(), so promote them to Int. The base class
            // already wraps BVNot in EmitBitvectorTruncation, which truncates the Int
            // result back to the bv width, so no suffix is needed here.
            var small = NativeTruncationSuffix(expr.Type) != "";  // Byte or Short
            TrParenExpr("", expr, wr, inLetExprBody, wStmts);
            wr.Write(small ? ".toInt().inv()" : ".inv()");
          } else {
            // Wide bv (dafny.BigInteger): emit BVNot as `-x - 1`, the two's-complement
            // complement, and let the surrounding EmitBitvectorTruncation do the width
            // wrap (mod 2^width brings the negative back to (2^width-1)-x). We can't use
            // ionspin's `.not()` (it throws on the sign-magnitude bignum), and spelling
            // the mask here would duplicate the wrap the truncation already applies.
            TrParenExpr("", expr, wr, inLetExprBody, wStmts);
            wr.Write(".negate().subtract(dafny.BigInteger.ONE)");
          }
          break;
        case ResolvedUnaryOp.Cardinality: {
            var collectionType = expr.Type.NormalizeToAncestorType().AsCollectionType;
            if (collectionType is MultiSetType) {
              TrParenExpr("", expr, wr, inLetExprBody, wStmts);
              wr.Write(".cardinality()");
            } else if (collectionType is SetType or MapType) {
              TrParenExpr("dafny.BigInteger.valueOf(", expr, wr, inLetExprBody, wStmts);
              wr.Write(".size().toLong())");
            } else if (expr.Type.IsArrayType) {
              TrParenExpr("dafny.BigInteger.valueOf((", expr, wr, inLetExprBody, wStmts);
              wr.Write(").dim0.toLong())");
            } else {
              TrParenExpr("dafny.BigInteger.valueOf(", expr, wr, inLetExprBody, wStmts);
              wr.Write(".length().toLong())");
            }
            break;
          }
        default:
          Contract.Assert(false); throw new Cce.UnreachableException();  // unexpected unary expression
      }
    }

    // Find the class with static methods like "divideUnsigned" for the type
    private string HelperClass(NativeType nt) {
      return AsKotlinNativeType(nt) == KotlinNativeType.Long ? "Long" : "Int";
    }

    protected override void CompileBinOp(BinaryExpr.ResolvedOpcode op, Type e0Type, Type e1Type, IOrigin tok,
      Type resultType, out string opString,
      out string preOpString, out string postOpString, out string callString, out string staticCallString,
      out bool reverseArguments, out bool truncateResult, out bool convertE1_to_int, out bool coerceE1,
      ConcreteSyntaxTree errorWr) {
      opString = null;
      preOpString = "";
      postOpString = "";
      callString = null;
      staticCallString = null;
      reverseArguments = false;
      truncateResult = false;
      convertE1_to_int = false;
      coerceE1 = false;

      void doPossiblyNativeBinOp(string o, string name, out string preOpS, out string opS,
        out string postOpS, out string callS, out string staticCallS) {
        if (AsNativeType(resultType) != null) {
          var nativeName = GetNativeTypeName(AsNativeType(resultType));
          if (o == ">>>" && resultType.AsBitVectorType is { Width: var width and (8 or 16 or 32 or 64) }) {
            // Solves https://github.com/dafny-lang/dafny/issues/3734
            preOpS = CastIfSmallNativeType(resultType);
            opS = null;
            postOpS = "";
            callS = null;
            staticCallS = $"{DafnyHelpersClass}.bv{width}ShiftRight";
          } else if (o == "<<" && resultType.AsBitVectorType is { Width: var width2 and (8 or 16 or 32 or 64) }) {
            // Solves https://github.com/dafny-lang/dafny/issues/3734. Byte/Short
            // (width 8/16) have no shl operator in Kotlin, so route ALL widths
            // through the helper (bv8/16ShiftLeft added for exactly this reason).
            preOpS = CastIfSmallNativeType(resultType);
            opS = null;
            postOpS = "";
            callS = null;
            staticCallS = $"{DafnyHelpersClass}.bv{width2}ShiftLeft";
          } else {
            // Kotlin: arithmetic on Byte/Short promotes to Int, so truncate the result
            // back with a postfix conversion (e.g. .toByte()).
            preOpS = "(";
            opS = o;
            postOpS = $"){NativeTruncationSuffix(resultType)}";
            callS = null;
            staticCallS = null;
          }
        } else {
          callS = name;
          preOpS = "";
          opS = null;
          postOpS = "";
          staticCallS = null;
        }
      }

      switch (op) {
        case BinaryExpr.ResolvedOpcode.BitwiseAnd:
          doPossiblyNativeBinOp("&", "and", out preOpString, out opString, out postOpString, out callString, out staticCallString);
          break;
        case BinaryExpr.ResolvedOpcode.BitwiseOr:
          doPossiblyNativeBinOp("|", "or", out preOpString, out opString, out postOpString, out callString, out staticCallString);
          break;
        case BinaryExpr.ResolvedOpcode.BitwiseXor:
          doPossiblyNativeBinOp("^", "xor", out preOpString, out opString, out postOpString, out callString, out staticCallString);
          break;
        case BinaryExpr.ResolvedOpcode.EqCommon: {
            var eqType = DatatypeWrapperEraser.SimplifyType(Options, e0Type);
            if (eqType.IsRefType) {
              // In Kotlin, == and != work fine for reference types without casting
              opString = "==";
            } else if (IsDirectlyComparable(eqType)) {
              opString = "==";
            } else {
              staticCallString = "dafny.Helpers.areEqual";
            }
            break;
          }
        case BinaryExpr.ResolvedOpcode.NeqCommon: {
            var eqType = DatatypeWrapperEraser.SimplifyType(Options, e0Type);
            if (eqType.IsRefType) {
              // In Kotlin, == and != work fine for reference types without casting
              opString = "!=";
            } else if (IsDirectlyComparable(eqType)) {
              opString = "!=";
            } else {
              preOpString = "!";
              staticCallString = "dafny.Helpers.areEqual";
            }
            break;
          }
        case BinaryExpr.ResolvedOpcode.Lt:
        case BinaryExpr.ResolvedOpcode.Le:
        case BinaryExpr.ResolvedOpcode.Ge:
        case BinaryExpr.ResolvedOpcode.Gt:
          var call = false;
          var argNative = AsNativeType(e0Type);
          if (argNative != null && argNative.LowerBound >= 0) {
            staticCallString = HelperClass(argNative) + ".compareUnsigned";
            call = true;
          } else if (argNative == null) {
            callString = "compareTo";
            call = true;
          }
          if (call) {
            switch (op) {
              case BinaryExpr.ResolvedOpcode.Lt:
                postOpString = " < 0";
                break;
              case BinaryExpr.ResolvedOpcode.Le:
                postOpString = " <= 0";
                break;
              case BinaryExpr.ResolvedOpcode.Ge:
                postOpString = " >= 0";
                break;
              case BinaryExpr.ResolvedOpcode.Gt:
                postOpString = " > 0";
                break;
              default:
                Contract.Assert(false);
                throw new Cce.UnreachableException();
            }
          } else {
            switch (op) {
              case BinaryExpr.ResolvedOpcode.Lt:
                opString = "<";
                break;
              case BinaryExpr.ResolvedOpcode.Le:
                opString = "<=";
                break;
              case BinaryExpr.ResolvedOpcode.Ge:
                opString = ">=";
                break;
              case BinaryExpr.ResolvedOpcode.Gt:
                opString = ">";
                break;
              default:
                Contract.Assert(false);
                throw new Cce.UnreachableException();
            }
          }
          break;
        case BinaryExpr.ResolvedOpcode.LeftShift:
          doPossiblyNativeBinOp("<<", "shiftLeft", out preOpString, out opString, out postOpString, out callString, out staticCallString);
          truncateResult = true;
          // For a bv65+ base (dafny.BigInteger), shiftLeft takes an Int. A native
          // e1 would emit as Byte/Short, and Kotlin has no implicit widening, so
          // convert the shift amount to Int whenever the base is non-native too.
          convertE1_to_int = AsNativeType(e1Type) == null || AsNativeType(resultType) == null;
          break;
        case BinaryExpr.ResolvedOpcode.RightShift:
          doPossiblyNativeBinOp(">>>", "shiftRight", out preOpString, out opString, out postOpString, out callString, out staticCallString);
          convertE1_to_int = AsNativeType(e1Type) == null || AsNativeType(resultType) == null;
          break;
        case BinaryExpr.ResolvedOpcode.Add:
          truncateResult = true;
          if (resultType.IsCharType) {
            // Dafny char arithmetic is code-point arithmetic. In unicode mode a char is
            // an Int code point (add directly); otherwise it's a Kotlin Char, so add the
            // .code values and convert back with .toChar().
            if (UnicodeCharEnabled) {
              preOpString = "("; opString = "+"; postOpString = ")";
            } else {
              preOpString = "(("; opString = ").code + ("; postOpString = ").code).toChar()";
            }
          } else {
            doPossiblyNativeBinOp("+", "add", out preOpString, out opString, out postOpString, out callString, out staticCallString);
          }
          break;
        case BinaryExpr.ResolvedOpcode.Sub:
          truncateResult = true;
          if (resultType.IsCharType) {
            if (UnicodeCharEnabled) {
              preOpString = "("; opString = "-"; postOpString = ")";
            } else {
              preOpString = "(("; opString = ").code - ("; postOpString = ").code).toChar()";
            }
          } else {
            doPossiblyNativeBinOp("-", "subtract", out preOpString, out opString, out postOpString, out callString, out staticCallString);
          }
          break;
        case BinaryExpr.ResolvedOpcode.Mul:
          doPossiblyNativeBinOp("*", "multiply", out preOpString, out opString, out postOpString, out callString, out staticCallString);
          truncateResult = true;
          break;
        case BinaryExpr.ResolvedOpcode.Div:
          if (NeedsEuclideanDivision(resultType)) {
            staticCallString = $"{DafnyEuclideanClass}.EuclideanDivision";
          } else if (AsNativeType(resultType) != null) {
            var nt = AsNativeType(resultType);
            if (nt.Sel == NativeType.Selection.Byte) {
              staticCallString = $"{DafnyHelpersClass}.divideUnsignedByte";
            } else if (nt.Sel == NativeType.Selection.UShort) {
              staticCallString = $"{DafnyHelpersClass}.divideUnsignedShort";
            } else {
              preOpString = CastIfSmallNativeType(resultType);
              staticCallString = HelperClass(AsNativeType(resultType)) + ".divideUnsigned";
            }
          } else {
            callString = "divide";
          }
          break;
        case BinaryExpr.ResolvedOpcode.Mod:
          if (NeedsEuclideanDivision(resultType)) {
            staticCallString = $"{DafnyEuclideanClass}.EuclideanModulus";
          } else if (AsNativeType(resultType) != null) {
            var nt = AsNativeType(resultType);
            if (nt.Sel == NativeType.Selection.Byte) {
              staticCallString = $"{DafnyHelpersClass}.remainderUnsignedByte";
            } else if (nt.Sel == NativeType.Selection.UShort) {
              staticCallString = $"{DafnyHelpersClass}.remainderUnsignedShort";
            } else {
              preOpString = CastIfSmallNativeType(resultType);
              staticCallString = HelperClass(AsNativeType(resultType)) + ".remainderUnsigned";
            }
          } else {
            callString = "mod";
          }
          break;
        case BinaryExpr.ResolvedOpcode.SetEq:
        case BinaryExpr.ResolvedOpcode.MultiSetEq:
        case BinaryExpr.ResolvedOpcode.SeqEq:
        case BinaryExpr.ResolvedOpcode.MapEq:
          callString = "equals";
          break;
        case BinaryExpr.ResolvedOpcode.ProperSubset:
        case BinaryExpr.ResolvedOpcode.ProperMultiSubset:
          callString = "isProperSubsetOf";
          break;
        case BinaryExpr.ResolvedOpcode.Subset:
        case BinaryExpr.ResolvedOpcode.MultiSubset:
          callString = "isSubsetOf";
          break;
        case BinaryExpr.ResolvedOpcode.Disjoint:
        case BinaryExpr.ResolvedOpcode.MultiSetDisjoint:
          callString = $"disjoint<{BoxedTypeName(e1Type.NormalizeToAncestorType().AsCollectionType.Arg, errorWr, tok)}>";
          break;
        case BinaryExpr.ResolvedOpcode.InSet:
        case BinaryExpr.ResolvedOpcode.InMultiSet:
        case BinaryExpr.ResolvedOpcode.InMap:
          callString = "contains";
          reverseArguments = true;
          coerceE1 = true;
          break;

        case BinaryExpr.ResolvedOpcode.Union:
          staticCallString = $"{DafnySetClass}.union<{BoxedTypeName(resultType.AsSetType.Arg, errorWr, tok)}>";
          break;
        case BinaryExpr.ResolvedOpcode.MultiSetUnion:
          staticCallString = $"{DafnyMultiSetClass}.union<{BoxedTypeName(resultType.AsMultiSetType.Arg, errorWr, tok)}>";
          break;
        case BinaryExpr.ResolvedOpcode.MapMerge:
          staticCallString = $"{DafnyMapClass}.merge<{BoxedTypeName(resultType.AsMapType.Domain, errorWr, tok)}, {BoxedTypeName(resultType.AsMapType.Range, errorWr, tok)}>";
          break;
        case BinaryExpr.ResolvedOpcode.Intersection:
          staticCallString = $"{DafnySetClass}.intersection<{BoxedTypeName(resultType.AsSetType.Arg, errorWr, tok)}>";
          break;
        case BinaryExpr.ResolvedOpcode.MultiSetIntersection:
          staticCallString = $"{DafnyMultiSetClass}.intersection<{BoxedTypeName(resultType.AsMultiSetType.Arg, errorWr, tok)}>";
          break;
        case BinaryExpr.ResolvedOpcode.SetDifference:
          staticCallString = $"{DafnySetClass}.difference<{BoxedTypeName(resultType.AsSetType.Arg, errorWr, tok)}>";
          break;
        case BinaryExpr.ResolvedOpcode.MultiSetDifference:
          staticCallString = $"{DafnyMultiSetClass}.difference<{BoxedTypeName(resultType.AsMultiSetType.Arg, errorWr, tok)}>";
          break;
        case BinaryExpr.ResolvedOpcode.MapSubtraction:
          staticCallString = $"{DafnyMapClass}.subtract<{BoxedTypeName(resultType.AsMapType.Domain, errorWr, tok)}, {BoxedTypeName(resultType.AsMapType.Range, errorWr, tok)}>";
          break;

        case BinaryExpr.ResolvedOpcode.ProperPrefix:
          callString = "isProperPrefixOf";
          break;
        case BinaryExpr.ResolvedOpcode.Prefix:
          callString = "isPrefixOf";
          break;
        case BinaryExpr.ResolvedOpcode.Concat:
          staticCallString = $"{DafnySeqClass}.concatenate<{BoxedTypeName(resultType.AsSeqType.Arg, errorWr, tok)}>";
          break;
        case BinaryExpr.ResolvedOpcode.InSeq:
          callString = "contains";
          reverseArguments = true;
          coerceE1 = true;
          break;
        default:
          base.CompileBinOp(op, e0Type, e1Type, tok, resultType,
            out opString, out preOpString, out postOpString, out callString, out staticCallString, out reverseArguments, out truncateResult, out convertE1_to_int, out coerceE1,
            errorWr);
          break;
      }
    }

    private void CreateTuple(int i, ConcreteSyntaxTree outputWr) {
      Contract.Requires(0 <= i);
      Contract.Requires(outputWr != null);

      var wrTop = outputWr.NewFile(Path.Combine("dafny", $"Tuple{i}.kt"));

      wrTop.WriteLine("package dafny;");
      wrTop.WriteLine();
      EmitSuppression(wrTop);
      wrTop.Write($"class Tuple{i}");
      if (i != 0) {
        wrTop.Write("<{0}>", Util.Comma(i, j => $"T{j}"));
      }

      var wr = wrTop.NewBlock("");
      for (var j = 0; j < i; j++) {
        wr.WriteLine("private val _{0}: T{0}", j);
      }
      wr.WriteLine();

      wr.Write("constructor({0}", Util.Comma(i, j => $"_{j}: T{j}"));
      var wrCtor = wr.NewBlock(")");
      for (var j = 0; j < i; j++) {
        wrCtor.WriteLine("this._{0} = _{0}", j);
      }

      wr.WriteLine();
      var typeParams = new List<TypeParameter>();
      for (var j = 0; j < i; j++) {
        typeParams.Add(new TypeParameter(SourceOrigin.NoToken, new Name($"T{j}"), TPVarianceSyntax.Covariant_Permissive));
      }
      var typeParamString = TypeParameters(typeParams);
      var initializer = string.Format("Default({0})", Util.Comma(i, j => $"_td_T{j}.defaultValue()"));
      EmitTypeDescriptorMethod(null, typeParams, $"Tuple{i}{typeParamString}", initializer, wr);

      // public static Tuple4<T0, T1, T2, T3> Default(dafny.TypeDescriptor<T0> _td_T0, dafny.TypeDescriptor<T1> _td_T1, dafny.TypeDescriptor<T2> _td_T2, dafny.TypeDescriptor<T3> _td_T3) {
      //   return new Tuple4<>(_td_T0.defaultValue(), _td_T1.defaultValue(), _td_T2.defaultValue(), _td_T3.defaultValue());
      // }
      wr.WriteLine();
      if (i == 0) {
        wr.Write("Tuple0");
      } else {
        wr.Write("<{1}> Tuple{0}<{1}>", i, Util.Comma(i, j => $"T{j}"));
      }
      wr.Write(" Default({0})", Util.Comma(i, j => $"T{j} {FormatDefaultTypeParameterValueName($"T{j}")}"));
      {
        var w = wr.NewBlock("");
        w.WriteLine("return create({0});", Util.Comma(i, j => $"{FormatDefaultTypeParameterValueName($"T{j}")}"));
      }

      // create method
      wr.WriteLine();
      if (i == 0) {
        wr.Write("Tuple0");
      } else {
        wr.Write("<{1}> Tuple{0}<{1}>", i, Util.Comma(i, j => $"T{j}"));
      }
      wr.Write(" create({0})", Util.Comma(i, j => $"T{j} _{j}"));
      {
        var w = wr.NewBlock("");
        w.WriteLine("return Tuple{0}({1});", i, Util.Comma(i, j => $"_{j}"));
      }

      wr.WriteLine();
      var wrEquals = wr.NewBlock("override fun equals(other: Any?): Boolean");
      wrEquals.WriteLine("if (this === other) return true");
      wrEquals.WriteLine("if (other == null) return false");
      wrEquals.WriteLine("if (this::class != other::class) return false");
      wrEquals.WriteLine($"val o = other as Tuple{i}");
      if (i != 0) {
        wrEquals.WriteLine("return {0}", Util.Comma(" && ", i, j => $"this._{j} == o._{j}"));
      } else {
        wrEquals.WriteLine("return true");
      }

      wr.WriteLine();
      var wrToString = wr.NewBlock("override fun toString(): String");
      wrToString.Write("return \"(\" + ");
      for (int j = 0; j < i; j++) {
        wrToString.Write($"(_{j}?.toString() ?: \"null\")");
        if (j != i - 1) {
          wrToString.Write(" + \", \" + ");
        }
      }
      wrToString.WriteLine(" + \")\"");

      wr.WriteLine();
      var wrHashCode = wr.NewBlock("override fun hashCode(): Int");
      wrHashCode.WriteLine("// GetHashCode method (Uses the djb2 algorithm)");
      wrHashCode.WriteLine(
        "// https://stackoverflow.com/questions/1579721/why-are-5381-and-33-so-important-in-the-djb2-algorithm");
      wrHashCode.WriteLine("long hash = 5381;");
      wrHashCode.WriteLine(
        "hash = ((hash << 5) + hash) + 0;"); // this is constructor 0 (in fact, it's the only constructor)
      for (int j = 0; j < i; j++) {
        wrHashCode.WriteLine("hash = ((hash << 5) + hash) + dafny.Helpers.hashCode(this._" + j + ");");
      }

      wrHashCode.WriteLine("return (int)hash;");

      for (int j = 0; j < i; j++) {
        wr.WriteLine();
        wr.WriteLine("T" + j + " dtor__" + j + "() { return this._" + j + "; }");
      }
    }

    protected override string TypeInitializationValue(Type type, ConcreteSyntaxTree wr, IOrigin tok, bool usePlaceboValue, bool constructTypeParameterDefaultsFromTypeDescriptors) {
      var xType = type.NormalizeExpandKeepConstraints();
      if (xType is BoolType) {
        return "false";
      } else if (xType is CharType) {
        // In unicode mode a char's native representation is the Int code point; default is 0.
        // (Java emitted `((int)'D')`, invalid Kotlin.)
        return UnicodeCharEnabled ? "0" : CharType.DefaultValueAsString;
      } else if (xType is IntType or BigOrdinalType) {
        return "dafny.BigInteger.ZERO";
      } else if (xType is RealType) {
        return $"{DafnyBigRationalClass}.ZERO";
      } else if (xType is BitvectorType) {
        var t = (BitvectorType)xType;
        return t.NativeType != null ? $"{CastIfSmallNativeType(t)}0" : "dafny.BigInteger.ZERO";
      } else if (xType is CollectionType collType) {
        string collName = CollectionTypeUnparameterizedName(collType);
        string argNames = BoxedTypeName(collType.Arg, wr, tok);
        if (xType is MapType mapType) {
          argNames += "," + BoxedTypeName(mapType.Range, wr, tok);
        }
        string td = "";
        if (xType is SeqType) {
          td = TypeDescriptor(collType.Arg, wr, tok);
        }
        return $"{collName}.empty<{argNames}>({td})";
      }

      var udt = (UserDefinedType)xType;
      var cl = udt.ResolvedClass;
      Contract.Assert(cl != null);
      if (cl is TypeParameter tp) {
        if (usePlaceboValue && !tp.Characteristics.HasCompiledValue) {
          // Emit a non-null-typed placebo from the threaded type descriptor rather than a
          // bare `null` (Kotlin forbids null for the non-null type parameter T).
          return $"({FormatTypeDescriptorVariable(tp.GetCompileName(Options))}.defaultValue() as {tp.GetCompileName(Options)})";
        } else if (constructTypeParameterDefaultsFromTypeDescriptors) {
          return $"{FormatTypeDescriptorVariable(tp.GetCompileName(Options))}.defaultValue()";
        } else {
          return FormatDefaultTypeParameterValue(tp);
        }
      } else if (cl is AbstractTypeDecl opaque) {
        return FormatDefaultTypeParameterValueName(opaque.GetCompileName(Options));
      } else if (cl is NewtypeDecl) {
        var td = (NewtypeDecl)cl;
        if (td.Witness != null) {
          return FullTypeName(udt) + ".Witness";
        } else if (td.NativeType != null) {
          return GetNativeDefault(td.NativeType);
        } else {
          return TypeInitializationValue(td.ConcreteBaseType(udt.TypeArgs), wr, tok, usePlaceboValue, constructTypeParameterDefaultsFromTypeDescriptors);
        }
      } else if (cl is SubsetTypeDecl) {
        var td = (SubsetTypeDecl)cl;
        if (td.WitnessKind == SubsetTypeDecl.WKind.Compiled) {
          var relevantTypeArgs = new List<Type>();
          for (int i = 0; i < td.TypeArgs.Count; i++) {
            if (NeedsTypeDescriptor(td.TypeArgs[i])) {
              relevantTypeArgs.Add(udt.TypeArgs[i]);
            }
          }
          string typeParameters = Util.Comma(relevantTypeArgs, arg => TypeDescriptor(arg, wr, tok));
          return $"{FullTypeName(udt)}.defaultValue({typeParameters})";
        } else if (td.WitnessKind == SubsetTypeDecl.WKind.Special) {
          // WKind.Special is only used with -->, ->, and non-null types:
          Contract.Assert(ArrowType.IsPartialArrowTypeName(td.Name) || ArrowType.IsTotalArrowTypeName(td.Name) || td is NonNullTypeDecl);
          if (ArrowType.IsPartialArrowTypeName(td.Name)) {
            // In Kotlin, we can't cast to nullable types, so just return null
            // The type system will infer the correct nullable type
            return "null";
          } else if (ArrowType.IsTotalArrowTypeName(td.Name)) {
            var rangeDefaultValue = TypeInitializationValue(udt.TypeArgs.Last(), wr, tok, usePlaceboValue, constructTypeParameterDefaultsFromTypeDescriptors);
            // Kotlin lambda: { x0: Ty0, x1: Ty1 -> rangeDefaultValue }
            var lparams = Util.Comma(udt.TypeArgs.Count - 1, i => $"{idGenerator.FreshId("x")}: {BoxedTypeName(udt.TypeArgs[i], wr, udt.Origin)}");
            return $"({{ {lparams} -> {rangeDefaultValue} }})";
          } else if (((NonNullTypeDecl)td).Class is ArrayClassDecl arrayClass) {
            // non-null array type; initialize with an empty array. Use the element type
            // descriptor's newArray so it works for both reference and native element
            // types (Kotlin has no `new T[0]`).
            var elType = udt.TypeArgs[0];
            TypeName_SplitArrayName(elType, out var innermostElementType, out var _);
            // For the flat backing array of dimension 0: T-descriptor.newArray(0).
            var bareArray = $"{TypeDescriptor(innermostElementType, wr, tok)}.newArray({Util.Comma(arrayClass.Dims, _ => "0")})";
            var zeros = Util.Repeat(arrayClass.Dims, "0, ");
            if (arrayClass.Dims == 1) {
              // An empty array default is a dafny.Array1<T> wrapper, not the raw storage.
              return $"{DafnyMultiArrayClass(1)}<{BoxedTypeName(elType, wr, tok)}>({TypeDescriptor(elType, wr, tok)}, 0, {bareArray})";
            } else {
              return $"{DafnyMultiArrayClass(arrayClass.Dims)}<{BoxedTypeName(elType, wr, tok)}>({TypeDescriptor(elType, wr, tok)}, {zeros}{bareArray} as kotlin.Array<Any?>)";
            }
          } else {
            return "null";
          }
        } else {
          return TypeInitializationValue(td.RhsWithArgument(udt.TypeArgs), wr, tok, usePlaceboValue, constructTypeParameterDefaultsFromTypeDescriptors);
        }
      } else if (cl is ClassLikeDecl or ArrowTypeDecl) {
        var boxed = BoxedTypeName(xType, wr, udt.Origin);
        var q = boxed.EndsWith("?") ? "" : "?"; // don't double up on already-nullable types (e.g. object? -> Any?)
        return $"(null as {boxed}{q})";
      } else if (cl is DatatypeDecl dt) {
        if (DatatypeWrapperEraser.GetInnerTypeOfErasableDatatypeWrapper(Options, dt, out var innerType)) {
          var typeSubstMap = TypeParameter.SubstitutionMap(dt.TypeArgs, udt.TypeArgs);
          return TypeInitializationValue(innerType.Subst(typeSubstMap), wr, tok, usePlaceboValue, constructTypeParameterDefaultsFromTypeDescriptors);
        }
        var s = FullTypeName(udt);
        var typeargs = "";
        var nonGhostTypeArgs = SelectNonGhost(cl, udt.TypeArgs);
        if (nonGhostTypeArgs.Count != 0) {
          typeargs = $"<{BoxedTypeNames(nonGhostTypeArgs, wr, udt.Origin)}>";
        }
        // In an auto-init context (like a field initializer) we may not have access to
        // all the type descriptors. For a non-generic datatype, `Dt.Default()` needs none
        // and gives a non-null value (Kotlin forbids a null placebo for the non-null
        // datatype). For a generic datatype we can't build the descriptors here, so keep
        // the null placebo (Dafny proves it isn't accessed).
        if (usePlaceboValue) {
          if (dt.TypeArgs.Count == 0) {
            return $"{s}.Default()";
          }
          return $"(null as {s}{typeargs}?)";
        }
        var wDefaultTypeArguments = new ConcreteSyntaxTree();
        var sep = "";
        WriteTypeDescriptors(dt, udt.TypeArgs, wDefaultTypeArguments, ref sep);
        var relevantTypeArgs = UsedTypeParameters(dt, udt.TypeArgs);
        var arguments = relevantTypeArgs.Comma(ta => DefaultValueCoercedIfNecessary(ta.Actual, wr, tok, constructTypeParameterDefaultsFromTypeDescriptors));
        if (relevantTypeArgs.Count == 0) {
          sep = "";
        }
        return $"{s}.Default{typeargs}({wDefaultTypeArguments}{sep}{arguments})";
      } else {
        Contract.Assert(false);
        throw new Cce.UnreachableException(); // unexpected type
      }
    }

    protected override ConcreteSyntaxTree DeclareLocalVar(string name, Type type, IOrigin tok, ConcreteSyntaxTree wr) {
      // For Kotlin, handle type names properly with nullable types
      var typeName = type != null ? TypeNameImpl(type, wr, tok, false, false, null) : "Any?";

      // Only Dafny nullable ref types map to Kotlin nullable types.
      if (type is { IsRefType: true, IsNonNullRefType: false }) {
        if (!typeName.EndsWith("?")) {
          typeName += "?";
        }
      }

      wr.Write("var {0}: {1} = ", name, typeName);
      var w = wr.Fork();
      wr.WriteLine("");
      return w;
    }

    protected override void DeclareLocalOutVar(string name, Type type, IOrigin tok, string rhs, bool useReturnStyleOuts, ConcreteSyntaxTree wr) {
      // The placebo for a non-null class/arrow type is a nullable null (bare `null` or
      // `(null as T?)`), which Kotlin rejects as the initializer of a non-null-typed local.
      // Out-params are always assigned before the (now linear) return, so drop the null
      // placebo and leave the var uninitialized — Kotlin's definite-assignment accepts it.
      if (rhs != null && (rhs == "null" || rhs.StartsWith("(null as "))
          && type is { IsRefType: true, IsNonNullRefType: true }) {
        rhs = null;
      }
      DeclareLocalVar(name, type, tok, false, rhs, wr);
    }

    protected override IClassWriter CreateTrait(string name, bool isExtern, List<TypeParameter> typeParameters /*?*/,
      TraitDecl trait, List<Type> superClasses, IOrigin tok, ConcreteSyntaxTree wr) {
      var filename = $"{ModulePath}/{IdProtect(name)}.kt";
      var w = wr.NewFile(filename);
      w.WriteLine($"// Interface {name}");
      w.WriteLine($"// Dafny trait {name} compiled into Kotlin");
      w.WriteLine($"package {ModuleName};");
      w.WriteLine();
      EmitSuppression(w); //TODO: Fix implementations so they do not need this suppression
      var typeParamString = TypeParameters(typeParameters);
      w.Write($"interface {IdProtect(name)}{typeParamString}");
      if (superClasses != null) {
        string sep = " : ";
        foreach (var tr in superClasses) {
          if (!tr.IsObject) {
            w.Write($"{sep}{TypeName(tr, w, tok)}");
            sep = ", ";
          }
        }
      }
      var instanceMemberWriter = w.NewBlock("");
      // Writing the _Companion class as a Kotlin object so its members are static.
      // (A Kotlin object can't have type parameters; trait companions hold only static
      // helpers, so this is fine for the supported subset.)
      filename = $"{ModulePath}/_Companion_{name}.kt";
      w = w.NewFile(filename);
      w.WriteLine($"// Interface {name}");
      w.WriteLine($"// Dafny trait {name} compiled into Kotlin");
      w.WriteLine($"package {ModuleName};");
      w.WriteLine();
      EmitSuppression(w); //TODO: Fix implementations so they do not need this suppression
      w.Write($"object _Companion_{name}");
      var staticMemberWriter = w.NewBlock("");
      var ctorBodyWriter = staticMemberWriter.Fork();

      if (Options.Get(KotlinBackend.LegacyDataConstructors)) {
        EmitTypeDescriptorMethod(null, typeParameters, name + typeParamString, initializer: null, wr: staticMemberWriter);
      }
      return new ClassWriter(this, instanceMemberWriter, ctorBodyWriter, staticMemberWriter, isTrait: true);
    }

    protected override void EmitDestructor(Action<ConcreteSyntaxTree> source, Formal dtor, int formalNonGhostIndex,
      DatatypeCtor ctor, Func<List<Type>> getTypeArgs, Type bvType, ConcreteSyntaxTree wr) {
      if (DatatypeWrapperEraser.IsErasableDatatypeWrapper(Options, ctor.EnclosingDatatype, out var coreDtor)) {
        Contract.Assert(coreDtor.CorrespondingFormals.Count == 1);
        Contract.Assert(dtor == coreDtor.CorrespondingFormals[0]); // any other destructor is a ghost
        source(wr);
        return;
      }
      string dtorName;
      if (ctor.EnclosingDatatype is TupleTypeDecl tupleTypeDecl) {
        Contract.Assert(tupleTypeDecl.NonGhostDims != 1); // such a tuple is an erasable-wrapper type, handled above
        dtorName = $"dtor__{dtor.NameForCompilation}()";
        wr = EmitCoercionIfNecessary(NativeObjectType, bvType, dtor.Origin, wr);
      } else {
        dtorName = FieldName(dtor, formalNonGhostIndex);
      }
      // For a codatatype, .Get() forces the thunk and returns the (abstract) base type,
      // so the cast to the concrete constructor must come AFTER .Get(), not before —
      // otherwise `(x as Dt_Ctor).Get().field` looks the field up on the base type.
      if (ctor.EnclosingDatatype is CoDatatypeDecl) {
        wr.Write("((");
        source(wr);
        wr.Write(").Get() as {0}).{1}", DtCtorName(ctor, getTypeArgs(), wr), dtorName);
      } else {
        wr.Write("((");
        source(wr);
        wr.Write(") as {0}).{1}", DtCtorName(ctor, getTypeArgs(), wr), dtorName);
      }
    }

    private void CreateLambdaFunctionInterface(int i, ConcreteSyntaxTree outputWr) {
      Contract.Requires(0 <= i);
      Contract.Requires(outputWr != null);

      var functionName = $"Function{i}";
      var wr = outputWr.NewFile(Path.Combine("dafny", $"{functionName}.kt"));

      var typeArgs = "<" + Util.Comma(i + 1, j => $"T{j}") + ">";

      wr.WriteLine("package dafny;");
      wr.WriteLine();
      wr.WriteLine("@FunctionalInterface");
      wr.Write($"interface {functionName}{typeArgs}");
      var wrMembers = wr.NewBlock("");
      wrMembers.Write($"T{i} apply(");
      wrMembers.Write(Util.Comma(i, j => $"T{j} t{j}"));
      wrMembers.WriteLine(");");

      EmitSuppression(wrMembers);
      wrMembers.Write($"{typeArgs} {DafnyTypeDescriptor}<{functionName}{typeArgs}> {TypeMethodName}(");
      wrMembers.Write(Util.Comma(i + 1, j => $"{DafnyTypeDescriptor}<T{j}> t{j}"));
      var wrTypeBody = wrMembers.NewBlock(")", "");
      // XXX This seems to allow non-nullable types to have null values (since
      // arrow types are allowed as "(0)"-constrained type arguments), but it's
      // consistent with other backends.
      wrTypeBody.Write($"return ({DafnyTypeDescriptor}<{functionName}{typeArgs}>) ({DafnyTypeDescriptor}<*>) {DafnyTypeDescriptor}.reference({functionName}::class.java)");
    }

    private void CreateDafnyArrays(int i, ConcreteSyntaxTree outputWr) {
      Contract.Requires(0 <= i);
      Contract.Requires(outputWr != null);

      var wrTop = outputWr.NewFile(Path.Combine("dafny", $"Array{i}.kt"));

      wrTop.WriteLine("package dafny;");
      wrTop.WriteLine();

      // All brackets on the underlying "real" array type, minus the innermost
      // pair.  The innermost array must be represented as an Object since it
      // could be of primitive type.
      var outerBrackets = Repeat("[]", i - 1);

      var dims = Enumerable.Range(0, i);
      var outerDims = Enumerable.Range(0, i - 1);

      var wr = wrTop.NewBlock($"class Array{i}<T>");

      wr.WriteLine($"val Object{outerBrackets} elmts;");
      wr.WriteLine($"private val {DafnyTypeDescriptor}<T> elmtType;");

      foreach (var j in dims) {
        wr.WriteLine($"val int dim{j};");
      }
      {
        var wrBody = wr.NewBlock($"Array{i}({DafnyTypeDescriptor}<T> elmtType, {Util.Comma(dims, j => $"int dim{j}")}, Object{outerBrackets} elmts)");
        wrBody.WriteLine("assert(elmts.javaClass.isArray)");
        wrBody.WriteLine("this.elmtType = elmtType;");
        foreach (var j in dims) {
          wrBody.WriteLine($"this.dim{j} = dim{j};");
        }
        wrBody.WriteLine("this.elmts = elmts;");
      }

      {
        var wrBody = wr.NewBlock($"T get({Util.Comma(dims, j => $"int i{j}")})");
        wrBody.Write("return elmtType.getArrayElement(elmts");
        foreach (var j in outerDims) {
          wrBody.Write($"[i{j}]");
        }
        wrBody.WriteLine($", i{i - 1});");
      }

      {
        var wrBody = wr.NewBlock($"fun set({Util.Comma(dims, j => $"int i{j}")}, T value)");
        wrBody.Write("elmtType.setArrayElement(elmts");
        foreach (var j in outerDims) {
          wrBody.Write($"[i{j}]");
        }
        wrBody.WriteLine($", i{i - 1}, value);");
      }

      {
        var body = wr.NewBlock("fun fill(T z)");
        var forBodyWr = body;
        for (int j = 0; j < i - 1; j++) {
          forBodyWr = forBodyWr.NewBlock($"for(int i{j} = 0; i{j} < dim{j}; i{j}++)");
        }
        forBodyWr.Write($"elmtType.fillArray(elmts");
        for (int j = 0; j < i - 1; j++) {
          forBodyWr.Write($"[i{j}]");
        }
        forBodyWr.WriteLine(", z);");
      }

      {
        var body = wr.NewBlock($"Array{i} fillThenReturn(T z)");
        body.WriteLine("fill(z);");
        body.WriteLine("return this;");
      }

      EmitSuppression(wr);
      wr.WriteLine($"private val TYPE: {DafnyTypeDescriptor}<Array{i}<*>> = ({DafnyTypeDescriptor}<Array{i}<*>>) ({DafnyTypeDescriptor}<*>) {DafnyTypeDescriptor}.reference(Array{i}::class.java)");
      EmitSuppression(wr);
      var wrTypeMethod = wr.NewBlock($"<T> {DafnyTypeDescriptor}<Array{i}<T>> {TypeMethodName}()");
      wrTypeMethod.WriteLine($"return ({DafnyTypeDescriptor}<Array{i}<T>>) ({DafnyTypeDescriptor}<*>) TYPE;");
    }

    protected override ConcreteSyntaxTree EmitTailCallStructure(MemberDecl member, ConcreteSyntaxTree wr) {
      if (!member.IsStatic && !NeedsCustomReceiver(member)) {
        var receiverType = UserDefinedType.FromTopLevelDecl(member.Origin, member.EnclosingClass);
        var receiverTypeName = TypeName(receiverType, wr, member.Origin);
        if (member.EnclosingClass.IsExtern(Options, out _, out _)) {
          receiverTypeName = FormatExternBaseClassName(receiverTypeName);
        }
        // `var` (not `val`): tail-call optimization reassigns `_this` on each iteration.
        wr.WriteLine("var _this: {0} = this", receiverTypeName);
      }
      // Kotlin function parameters are immutable (val), but tail-call optimization
      // reassigns the in-parameters. Shadow each in-parameter with a mutable local.
      if (member is MethodOrFunction mf) {
        foreach (var p in mf.Ins) {
          if (!p.IsGhost) {
            var pName = IdName(p);
            wr.WriteLine($"var {pName} = {pName}");
          }
        }
      }
      return wr.NewBlock("TAIL_CALL_START@ while (true)");
    }

    protected override void EmitJumpToTailCallStart(ConcreteSyntaxTree wr) {
      wr.WriteLine("continue@TAIL_CALL_START");
    }

    protected override ConcreteSyntaxTree CreateForeachLoop(
      string tmpVarName, Type collectionElementType, IOrigin tok, out ConcreteSyntaxTree collectionWriter, ConcreteSyntaxTree wr) {

      // We may have to coerce from the boxed type used in collections
      var needsCoercion = IsCoercionNecessary(NativeObjectType, collectionElementType);
      var loopVarName = needsCoercion ? ProtectedFreshId(tmpVarName + "_boxed") : tmpVarName;
      // Kotlin: `for (x in coll) { ... }` — no element type before the variable, `in`
      // instead of Java's `:`. Any element-type coercion is done inside the body.
      wr.Write($"for ({loopVarName} in ");
      collectionWriter = wr.Fork();
      var wwr = wr.NewBlock(")");
      if (needsCoercion) {
        wwr.Write($"val {tmpVarName}: {TypeName(collectionElementType, wr, tok)} = ");
        var coercedWwr = EmitCoercionIfNecessary(NativeObjectType, collectionElementType, tok, wwr);
        coercedWwr.Write(loopVarName);
        wwr.WriteLine("");
      }
      return wwr;
    }

    protected override Action<ConcreteSyntaxTree> GetSubtypeCondition(string tmpVarName, Type boundVarType, IOrigin tok, ConcreteSyntaxTree wPreconditions) {
      string typeTest;

      if (boundVarType.IsRefType) {
        if (boundVarType.IsObject || boundVarType.IsObjectQ) {
          typeTest = "true";
        } else {
          typeTest = $"{tmpVarName} is {TypeName(boundVarType, wPreconditions, tok)}";
        }
        if (boundVarType.IsNonNullRefType) {
          typeTest = $"{tmpVarName} != null && {typeTest}";
        } else {
          typeTest = $"{tmpVarName} == null || {typeTest}";
        }
      } else {
        typeTest = "true";
      }

      return typeTest == null ? null : wr => wr.Write(typeTest);
    }

    protected override void EmitDowncastVariableAssignment(string boundVarName, Type boundVarType, string tmpVarName,
      Type sourceType, bool introduceBoundVar, IOrigin tok, ConcreteSyntaxTree wr) {

      var typeName = TypeName(boundVarType, wr, tok);
      // Kotlin: `var name: Type = tmp as Type` (or `name = tmp as Type` for reassignment).
      if (introduceBoundVar) {
        wr.WriteLine("var {0}: {1} = {2} as {1}", boundVarName, typeName, tmpVarName);
      } else {
        wr.WriteLine("{0} = {1} as {2}", boundVarName, tmpVarName, typeName);
      }
    }

    protected override ConcreteSyntaxTree CreateForeachIngredientLoop(string boundVarName, int L, string tupleTypeArgs, out ConcreteSyntaxTree collectionWriter, ConcreteSyntaxTree wr) {
      wr.Write($"for ({boundVarName} in ");
      collectionWriter = wr.Fork();
      return wr.NewBlock(")");
    }

    protected override void EmitSetBuilder_Add(CollectionType ct, string collName, Expression elmt, bool inLetExprBody, ConcreteSyntaxTree wr) {
      if (ct is SetType) {
        var wStmts = wr.Fork();
        wr.Write($"{collName}.add(");
        var coercedWr = EmitCoercionIfNecessary(elmt.Type, NativeObjectType, elmt.Origin, wr);
        coercedWr.Append(Expr(elmt, inLetExprBody, wStmts));
        wr.WriteLine(");");
      } else {
        Contract.Assume(false);  // unexpected collection type
      }
    }

    protected override void GetCollectionBuilder_Build(CollectionType ct, IOrigin tok, string collName,
      ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmt) {
      if (ct is SetType) {
        var typeName = BoxedTypeName(ct.Arg, wr, tok);
        wr.Write($"{DafnySetClass}<{typeName}>({collName})");
      } else if (ct is MapType) {
        var mt = (MapType)ct;
        var domtypeName = BoxedTypeName(mt.Domain, wr, tok);
        var rantypeName = BoxedTypeName(mt.Range, wr, tok);
        wr.Write($"{DafnyMapClass}<{domtypeName},{rantypeName}>({collName})");
      } else {
        Contract.Assume(false);  // unexpected collection type
        throw new Cce.UnreachableException();  // please compiler
      }
    }

    protected override ConcreteSyntaxTree CreateLabeledCode(string label, bool createContinueLabel, ConcreteSyntaxTree wr) {
      var prefix = createContinueLabel ? "continue_" : "goto_";
      // Kotlin cannot `break` out of an arbitrary labeled block (break/continue target
      // loops only). Dafny's label/break-to-label (e.g. assign-such-that) is modelled
      // with a `run { ... }` lambda labeled ON THE LAMBDA, left early via `return@label`.
      // So `name: { body }` (Java) becomes `run name@{ body }` and `break name`
      // becomes `return@name`. The label must sit on the lambda `{`, not on `run`.
      wr.Write($"run {prefix}{label}@");
      return wr.NewBlock("", null, BlockStyle.Brace, BlockStyle.NewlineBrace);
    }

    protected override void EmitBreak(string label, ConcreteSyntaxTree wr) {
      // No label => break a loop; labelled => leave the labeled run{} block.
      wr.WriteLine(label == null ? "break" : $"return@goto_{label}");
    }

    protected override void EmitContinue(string label, ConcreteSyntaxTree wr) {
      // `continue` leaves the per-iteration `run continue_<label>@{ ... }` block, which
      // ends the current iteration (Kotlin: return@label, not Java `break continue_<label>`).
      wr.WriteLine($"return@continue_{label}");
    }

    protected override void EmitAbsurd(string message, ConcreteSyntaxTree wr) {
      if (message == null) {
        message = "unexpected control point";
      }

      // A bare `throw` (no `if (true) { ... }` wrapper as the Java backend uses). In Kotlin
      // unreachable code after a throw is only a warning, not an error, and — crucially — a
      // bare throw is a terminal expression, so the definite-assignment analysis sees that
      // the fall-through path (where an assign-such-that target would be unassigned) can't
      // be reached. The `if (true)` wrapper would hide that, leaving the target "must be
      // initialized".
      wr.WriteLine($"throw IllegalArgumentException(\"{message}\")");
    }

    protected override void EmitAbsurd(string message, ConcreteSyntaxTree wr, bool needIterLimit) {
      if (!needIterLimit) {
        EmitAbsurd(message, wr);
      }
    }

    protected override void EmitHalt(IOrigin tok, Expression messageExpr, ConcreteSyntaxTree wr) {
      var wStmts = wr.Fork();
      wr.Write("throw dafny.DafnyHaltException(");
      if (tok != null) {
        EmitStringLiteral(tok.OriginToString(Options) + ": ", true, wr);
        wr.Write(" + ");
      }

      EmitToString(wr, messageExpr, wStmts);
      wr.WriteLine(");");
    }

    protected override IClassWriter DeclareNewtype(NewtypeDecl nt, ConcreteSyntaxTree wr) {
      var cw = (ClassWriter)CreateClass(IdProtect(nt.EnclosingModuleDefinition.GetCompileName(Options)), nt, wr);
      var w = cw.StaticMemberWriter;
      if (nt.NativeType != null) {
        var nativeType = GetBoxedNativeTypeName(nt.NativeType);
        var wEnum = w.NewNamedBlock($"fun IntegerRange(lo: dafny.BigInteger, hi: dafny.BigInteger): MutableList<{nativeType}>");
        wEnum.WriteLine($"val arr: MutableList<{nativeType}> = mutableListOf()");
        var conv = NativeConversionMethod(nt.NativeType);
        wEnum.WriteLine($"var j = lo");
        wEnum.WriteLine($"while (j.compareTo(hi) < 0) {{ arr.add(j{conv}); j = j.add(dafny.BigInteger.ONE) }}");
        wEnum.WriteLine("return arr");
      }
      if (nt.WitnessKind == SubsetTypeDecl.WKind.Compiled) {
        var wStmts = w.Fork();
        var witness = new ConcreteSyntaxTree(w.RelativeIndentLevel);
        witness.Append(Expr(nt.Witness, false, wStmts));
        if (nt.NativeType == null) {
          cw.DeclareField("Witness", nt, true, true, nt.BaseType, nt.Origin, witness.ToString(), null);
        } else {
          var nativeType = GetNativeTypeName(nt.NativeType);
          var conv = NativeConversionMethod(nt.NativeType);
          // Convert the witness (possibly a BigInteger) to the native type. Kotlin:
          // `val Witness: Int = (<expr> as Number).toInt()` (kotlin.Number, whose
          // toInt/toLong/... BigInteger implements on the JVM).
          w.Write($"val Witness: {nativeType} = ((");
          w.Append(witness);
          w.WriteLine($") as Number){conv}");
        }
      }

      GenerateIsMethod(nt, cw.StaticMemberWriter);

      if (nt.Traits.Count != 0) {
        // A newtype that extends a trait requires the experimental
        // --general-traits=full mode. Full member dispatch for that combination
        // is not yet implemented for Kotlin, so reject it cleanly rather than
        // emit code that does not compile. (Feature.Traits can't be used here —
        // Kotlin supports ordinary traits — so emit a plain compile error.)
        Error(GeneratorErrors.ErrorId.c_unsupported_feature, nt.Origin, cw.InstanceMemberWriter,
          "the Kotlin backend does not support a newtype extending a trait");
      }

      return cw;
    }

    void GenerateIsMethod(RedirectingTypeDecl declWithConstraints, ConcreteSyntaxTree wr) {
      Contract.Requires(declWithConstraints is SubsetTypeDecl or NewtypeDecl);

      if (declWithConstraints.ConstraintIsCompilable) {
        var type = UserDefinedType.FromTopLevelDecl(declWithConstraints.Tok, (TopLevelDecl)declWithConstraints);

        wr.Write($"fun {TypeParameters(declWithConstraints.TypeArgs, " ")}{IsMethodName}(");

        var wCtorParams = new ConcreteSyntaxTree();
        var count = EmitTypeDescriptorsForClass(declWithConstraints.TypeArgs, (TopLevelDecl)declWithConstraints,
          null, wCtorParams, null, null);
        if (count != 0) {
          wr.Write($"{wCtorParams}, ");
        }

        var sourceFormal = new Formal(declWithConstraints.Tok, "_source", type, true, false, null);
        var typeName = TypeName(type, wr, declWithConstraints.Tok);
        var wrBody = wr.NewBlock($"{IdName(sourceFormal)}: {typeName}): Boolean");
        GenerateIsMethodBody(declWithConstraints, sourceFormal, wrBody);
      }
    }

    protected override string ArrayIndexToNativeInt(string s, Type type) {
      var nt = AsNativeType(type);
      if (nt == null) {
        return $"({s}).toInt()";
      } else if (nt.Sel == NativeType.Selection.Int || nt.Sel == NativeType.Selection.UInt) {
        return s;
      } else if (IsUnsignedKotlinNativeType(nt)) {
        return $"{DafnyHelpersClass}.unsignedToInt({s})";
      } else {
        return $"{DafnyHelpersClass}.toInt({s})";
      }
    }

    // if checkRange is false, msg is ignored
    // if checkRange is true and msg is null and the value is out of range, a generic message is emitted
    // if checkRange is true and msg is not null and the value is out of range, msg is emitted in the error message
    private void TrExprAsInt(Expression expr, ConcreteSyntaxTree wr, bool inLetExprBody, ConcreteSyntaxTree wStmts,
      bool checkRange = false, string msg = null) {
      var wrExpr = new ConcreteSyntaxTree();
      wrExpr.Append(Expr(expr, inLetExprBody, wStmts));
      TrExprAsInt(wrExpr.ToString(), expr.Type, wr, checkRange, msg);
    }

    // if checkRange is false, msg is ignored
    // if checkRange is true and msg is null and the value is out of range, a generic message is emitted
    // if checkRange is true and msg is not null and the value is out of range, msg is emitted in the error message
    private void TrExprAsInt(string expr, Type type, ConcreteSyntaxTree wr, bool checkRange = false, string msg = null) {
      var nt = AsNativeType(type);
      if (nt == null) {
        wr.Write($"{DafnyHelpersClass}.toInt" + (checkRange ? "Checked(" : "("));
        wr.Write($"({expr})");
        if (checkRange) {
          wr.Write(msg == null ? ", null" : $", \"{msg}\"");
        }

        wr.Write(")");
      } else if (nt.Sel == NativeType.Selection.Int || nt.Sel == NativeType.Selection.UInt) {
        wr.Write(expr);
      } else if (IsUnsignedKotlinNativeType(nt)) {
        wr.Write($"{DafnyHelpersClass}.unsignedToInt" + (checkRange ? "Checked(" : "("));
        wr.Write(expr);
        if (checkRange) {
          wr.Write(msg == null ? ", null" : $", \"{msg}\"");
        }

        wr.Write(")");
      } else {
        // Signed small native type (Byte/Short): widening to Int always fits, and the
        // runtime's toInt/toIntChecked has no Byte/Short overload — just use .toInt().
        wr.Write($"({expr}).toInt()");
      }
    }

    private void TrParenExprAsInt(Expression expr, ConcreteSyntaxTree wr, bool inLetExprBody, ConcreteSyntaxTree wStmts) {
      wr.Write("(");
      TrExprAsInt(expr, wr, inLetExprBody, wStmts);
      wr.Write(")");
    }

    protected override void EmitNewArray(Type elementType, IOrigin tok, List<string> dimensions,
        bool mustInitialize, [CanBeNull] string exampleElement, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      // All arrays are dafny.ArrayN<T> wrappers: ArrayN(typeDescriptor, dim0, dim1, ..., elmts).
      // The element storage is produced by TypeDescriptor.newArray(dims...), typed Any.
      wr.Write($"{DafnyMultiArrayClass(dimensions.Count)}<{ActualTypeArgument(DatatypeWrapperEraser.SimplifyType(Options, elementType), TypeParameter.TPVariance.Non, wr, tok)}>({TypeDescriptor(elementType, wr, tok)}, ");
      foreach (var dim in dimensions) {
        TrExprAsInt(dim, Type.Int, wr, checkRange: true, msg: "Java arrays may be no larger than the maximum 32-bit signed int");
        wr.Write(", ");
      }
      var wBareArray = wr.Fork();
      wr.Write(")");
      if (mustInitialize) {
        wr.Write($".fillThenReturn({DefaultValueCoercedIfNecessary(elementType, wr, tok, true)})");
      }

      // For multi-dim arrays the ArrayN wrapper expects the outer storage as
      // kotlin.Array<Any?>; newArray returns Any, so cast.
      var multiDim = dimensions.Count > 1;
      if (multiDim) {
        wBareArray.Write("(");
      }
      wBareArray.Write($"{TypeDescriptor(elementType, wr, tok)}.newArray(");
      var sep = "";
      foreach (var dim in dimensions) {
        wBareArray.Write(sep);
        TrExprAsInt(dim, Type.Int, wBareArray, checkRange: true, msg: "Java arrays may be no larger than the maximum 32-bit signed int");
        sep = ", ";
      }
      wBareArray.Write(")");
      if (multiDim) {
        wBareArray.Write(" as kotlin.Array<Any?>)");
      }
    }

    protected override ConcreteSyntaxTree EmitBetaRedex(List<string> boundVars, List<Expression> arguments,
      List<Type> boundTypes, Type resultType, IOrigin resultTok, bool inLetExprBody, ConcreteSyntaxTree wr,
      ref ConcreteSyntaxTree wStmts) {
      // Emit an inline Kotlin anonymous function applied immediately:
      //   (fun(p: T, ...): R { return <body> })(args...)
      // Anonymous functions close over enclosing locals and support block bodies with
      // explicit "return", avoiding the broken hoisted-"fun NAME" + "::NAME" capture pattern.
      var paramList = boundVars.Zip(boundTypes,
        (boundVar, type) => $"{boundVar}: {BoxedTypeName(type, wr, resultTok)}"
      ).Comma();
      wr.Write("(");
      var wrBody = wr.NewBlock($"fun({paramList}): {BoxedTypeName(resultType, wr, resultTok)}");
      var bodyReturn = wrBody.Fork();
      var returnStmts = EmitReturnExpr(bodyReturn);
      wr.Write(")");

      // Apply the anonymous function to the arguments.
      TrExprList(arguments, wr, inLetExprBody, wStmts);

      return returnStmts;
    }

    protected override ConcreteSyntaxTree CreateForLoop(string indexVar, Action<ConcreteSyntaxTree> boundAction, ConcreteSyntaxTree wr, string start = null) {
      start = start ?? "dafny.BigInteger.ZERO";
      var boundWriter = new ConcreteSyntaxTree();
      boundAction(boundWriter);
      var bound = boundWriter.ToString();
      // Kotlin while loop: var {indexVar} = {start}; while ({indexVar}.compareTo({bound}) < 0) { ... then {indexVar} = {indexVar}.add(...) }
      wr.WriteLine($"var {indexVar} = {start}");
      var whileBlock = wr.NewBlock($"while ({indexVar}.compareTo({bound}) < 0)");
      // Body statements go directly into the while block. We must NOT wrap them in a bare
      // "{ ... }" nested block: in Kotlin that is a lambda literal (an unused expression),
      // not a scope, so the body would never execute. Fork a region for the body and emit
      // the loop-index increment AFTER it so the while loop terminates.
      var bodyBlock = whileBlock.Fork();
      whileBlock.WriteLine($"{indexVar} = {indexVar}.add(dafny.BigInteger.ONE)");
      return bodyBlock;
    }

    protected override ConcreteSyntaxTree EmitForStmt(IOrigin tok, IVariable loopIndex, bool goingUp,
      string/*?*/ endVarName,
      List<Statement> body, List<Label> labels, ConcreteSyntaxTree wr) {

      var nativeType = AsNativeType(loopIndex.Type);
      var indexVarName = loopIndex.GetOrCreateCompileName(currentIdGenerator);

      // Kotlin style: var declaration, then while loop
      wr.Write($"var {indexVarName} = ");
      var startWr = wr.Fork();
      wr.WriteLine(";");

      // Generate while condition
      string whileCondition = "";
      if (goingUp) {
        if (endVarName != null) {
          if (nativeType == null) {
            whileCondition = $"{indexVarName}.compareTo({endVarName}) < 0";
          } else if (0 <= nativeType.LowerBound) {
            whileCondition = $"{HelperClass(nativeType)}.compareUnsigned({indexVarName}, {endVarName}) < 0";
          } else {
            whileCondition = $"{indexVarName} < {endVarName}";
          }
        }
      } else {
        if (endVarName != null) {
          if (nativeType == null) {
            whileCondition = $"{endVarName}.compareTo({indexVarName}) < 0";
          } else if (0 <= nativeType.LowerBound) {
            whileCondition = $"{HelperClass(nativeType)}.compareUnsigned({endVarName}, {indexVarName}) < 0";
          } else {
            whileCondition = $"{endVarName} < {indexVarName}";
          }
        }
      }

      // An unbounded for-loop (no end bound) has no guard; Kotlin needs `while (true)`.
      var bodyWr = wr.NewBlock($"while ({(whileCondition == "" ? "true" : whileCondition)})");
      bodyWr = EmitContinueLabel(labels, bodyWr);
      TrStmtList(body, bodyWr);

      // Emit increment/decrement at end of loop body
      if (goingUp) {
        if (nativeType == null) {
          bodyWr.WriteLine($"{indexVarName} = {indexVarName}.add(dafny.BigInteger.ONE);");
        } else {
          bodyWr.WriteLine($"{indexVarName}++;");
        }
      } else {
        if (nativeType == null) {
          bodyWr.WriteLine($"{indexVarName} = {indexVarName}.subtract(dafny.BigInteger.ONE);");
        } else {
          bodyWr.WriteLine($"{indexVarName}--;");
        }
      }

      return startWr;
    }

    protected override string GetHelperModuleName() => DafnyHelpersClass;

    protected override void EmitEmptyTupleList(string tupleTypeArgs, ConcreteSyntaxTree wr) {
      wr.WriteLine("mutableListOf()");
    }

    protected override ConcreteSyntaxTree EmitAddTupleToList(string ingredients, string tupleTypeArgs, ConcreteSyntaxTree wr) {
      // FIXME: tupleTypeArgs is wrong because it already got generated from
      // TypeName (with unboxed being the default)  :-(
      wr.Write($"{ingredients}.add({DafnyTupleClassPrefix}");
      var wrTuple = wr.Fork();
      wr.Write("));");
      return wrTuple;
    }

    protected override void EmitExprAsNativeInt(Expression expr, bool inLetExprBody, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      // Kotlin uses .toInt() (BigInteger.toInt() on the JVM), not Java's .intValue().
      TrParenExpr(expr, wr, inLetExprBody, wStmts);
      wr.Write(".toInt()");
    }

    protected override void EmitTupleSelect(string prefix, int i, ConcreteSyntaxTree wr) {
      wr.Write($"{prefix}.dtor__{i}()");
    }

    protected override void EmitApplyExpr(Type functionType, IOrigin tok, Expression function, List<Expression> arguments, bool inLetExprBody, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      wr = EmitCoercionIfNecessary(NativeObjectType, functionType.AsArrowType.Result, tok, wr);
      TrParenExpr(function, wr, inLetExprBody, wStmts);
      wr.Write(".invoke");
      TrExprList(arguments, wr, inLetExprBody, wStmts, typeAt: _ => NativeObjectType);
    }

    protected override bool NeedsCastFromTypeParameter => true;

    protected override bool TargetSubtypingRequiresEqualTypeArguments(Type type) {
      return type.NormalizeToAncestorType().AsCollectionType == null;
    }

    protected override bool IsCoercionNecessary(Type/*?*/ from, Type/*?*/ to) {
      from = from == null ? null : DatatypeWrapperEraser.SimplifyTypeAndTrimNewtypes(Options, from);
      to = to == null ? null : DatatypeWrapperEraser.SimplifyTypeAndTrimNewtypes(Options, to);

      if (to == NativeObjectType) {
        return false;
      }
      if (from == NativeObjectType) {
        return true;
      }

      if (UnicodeCharEnabled && ((IsNativeObjectType(from) && to.IsCharType) || (from.IsCharType && IsNativeObjectType(to)))) {
        // Need to box from int to CodePoint, or unbox from CodePoint to int
        return true;
      }

      if (from.IsArrayType && to.IsArrayType) {
        var dims = from.AsArrayType.Dims;
        Contract.Assert(dims == to.AsArrayType.Dims);
        if (dims > 1) {
          return false;
        }

        var udtFrom = (UserDefinedType)from.NormalizeExpand();
        var udtTo = (UserDefinedType)to.NormalizeExpand();
        return udtFrom.TypeArgs[0].IsTypeParameter && !udtTo.TypeArgs[0].IsTypeParameter;
      }

      return false;
    }

    protected override Type TypeForCoercion(Type type) {
      return NativeObjectType;
    }

    // We use null to represent Any, as that's a decent
    // default native type for "no type information".
    // We don't use the SpecialNativeType approach that the Go compiler
    // uses for string because that kind of compiler-specific Type implementation
    // doesn't fit well into the generic logic on Types
    // (see for example https://github.com/dafny-lang/dafny/issues/2989).
    private static readonly Type NativeObjectType = null;

    private bool IsNativeObjectType(Type type) {
      return type == NativeObjectType || type.IsTypeParameter;
    }

    protected override ConcreteSyntaxTree FromFatPointer(Type type, ConcreteSyntaxTree wr) {
      if (type.HasFatPointer) {
        var w = wr.ForkInParens();
        wr.Write("._value");
        return w;
      } else {
        return wr;
      }
    }

    protected override ConcreteSyntaxTree ToFatPointer(Type type, ConcreteSyntaxTree wr) {
      if (type.HasFatPointer) {
        wr.Write($"{type.AsNewtype.GetFullCompileName(Options)}");
        return wr.ForkInParens();
      } else {
        return wr;
      }
    }

    protected override ConcreteSyntaxTree EmitCoercionIfNecessary(Type/*?*/ from, Type/*?*/ to, IOrigin tok, ConcreteSyntaxTree wr, Type toOrig = null) {
      if (toOrig != null) {
        to = toOrig;
      }

      if (from != null && to != null && from.IsTraitType && to.AsNewtype != null) {
        return FromFatPointer(to, wr);
      }
      if (from != null && to != null && from.AsNewtype != null && to.IsTraitType && (enclosingMethod != null || enclosingFunction != null)) {
        return ToFatPointer(from, wr);
      }

      from = from == null ? null : DatatypeWrapperEraser.SimplifyTypeAndTrimNewtypes(Options, from);
      to = to == null ? null : DatatypeWrapperEraser.SimplifyTypeAndTrimNewtypes(Options, to);

      if (UnicodeCharEnabled) {
        // Need to box from int to CodePoint, or unbox from CodePoint to int
        if (IsNativeObjectType(from) && to is { IsCharType: true }) {
          wr.Write("((");
          var w = wr.Fork();
          wr.Write(") as dafny.CodePoint).value()");
          return w;
        }

        if (from is { IsCharType: true } && IsNativeObjectType(to)) {
          wr.Write("dafny.CodePoint.valueOf(");
          var w = wr.Fork();
          wr.Write(")");
          return w;
        }
      }

      if (IsCoercionNecessary(from, to)) {
        return EmitDowncast(from, to, tok, wr);
      }

      return wr;
    }

    protected override ConcreteSyntaxTree EmitDowncast(Type from, Type to, IOrigin tok, ConcreteSyntaxTree wr) {
      var w = new ConcreteSyntaxTree();
      // Numeric native target types can't use 'as' (e.g. Int as Byte throws); use the
      // postfix numeric conversion instead.
      var toNative = to == null ? null : AsNativeType(to);
      if (toNative != null) {
        wr.Write("(");
        w = wr.ForkInParens();
        wr.Write($"){NativeConversionMethod(toNative)}");
      } else if (from != null && to != null && from.IsTraitType && to.AsNewtype != null) {
        wr.Format($"(({w}) as {to.AsNewtype.GetFullCompileName(Options)})");
      } else if (from != null && to != null && from.AsNewtype != null && to.IsTraitType) {
        wr.Format($"(({w}) as {TypeName(to, wr, tok)})");
      } else {
        // Kotlin's 'as' cast subsumes Java's (T)(Object)x trick; unchecked warnings are suppressed.
        wr.Write("((");
        w = wr.Fork();
        wr.Write($") as {TypeName(to, wr, tok)})");
      }
      return w;
    }

    protected override ConcreteSyntaxTree EmitCoercionToNativeInt(ConcreteSyntaxTree wr) {
      wr.Write("((");
      var w = wr.Fork();
      wr.Write(") as dafny.BigInteger).toInt()");
      return w;
    }

    protected override ConcreteSyntaxTree CreateDoublingForLoop(string indexVar, int start, ConcreteSyntaxTree wr) {
      return wr.NewNamedBlock($"for (var {indexVar} = dafny.BigInteger.valueOf({start}L); ; {indexVar} = {indexVar}.multiply(dafny.BigInteger.valueOf(2L)))");
    }

    protected override void EmitIsZero(string varName, ConcreteSyntaxTree wr) {
      wr.Write($"{varName}.equals(dafny.BigInteger.ZERO)");
    }

    protected override void EmitDecrementVar(string varName, ConcreteSyntaxTree wr) {
      wr.WriteLine($"{varName} = {varName}.subtract(dafny.BigInteger.ONE);");
    }

    protected override void EmitIncrementVar(string varName, ConcreteSyntaxTree wr) {
      wr.WriteLine($"{varName} = {varName}.add(dafny.BigInteger.ONE);");
    }

    protected override void EmitSingleValueGenerator(Expression e, bool inLetExprBody, string type, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      wr.Write("listOf");
      TrParenExpr(e, wr, inLetExprBody, wStmts);
    }

    protected override ConcreteSyntaxTree CreateIIFE1(int source, Type resultType, IOrigin resultTok, string bvName,
        ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      // Immediately-invoked anonymous function (allows `return` in the body, unlike a
      // Kotlin lambda): (fun(bv: BigInteger): T { ...; return x })(source)
      wr.Write($"(fun({bvName}: dafny.BigInteger): {BoxedTypeName(resultType, wr, resultTok)} ");
      var w = wr.NewBigExprBlock("", $")(dafny.BigInteger.valueOf({source})))");
      return w;
    }

    protected override ConcreteSyntaxTree EmitMapBuilder_Add(MapType mt, IOrigin tok, string collName, Expression term, bool inLetExprBody, ConcreteSyntaxTree wr) {
      var wStmts = wr.Fork();
      wr.Write($"{collName}.put(");
      var termLeftWriter = wr.Fork();
      wr.Write(",");
      wr.Append(Expr(term, inLetExprBody, wStmts));
      wr.WriteLine(");");
      return termLeftWriter;
    }

    protected override void EmitSeqConstructionExpr(SeqConstructionExpr expr, bool inLetExprBody, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      wr.Write($"{DafnySeqClass}.Create({TypeDescriptor(expr.Type.NormalizeToAncestorType().AsCollectionType.Arg, wr, expr.Origin)}, ");
      var size = expr.N;
      if (AsKotlinNativeType(size.Type) is { }) {
        size = new ConversionExpr(expr.N.Origin, size, new IntType());
      }
      var sizeWr = Expr(size, inLetExprBody, wStmts);
      wr.Append(sizeWr);
      wr.Write(", ");
      wr.Append(Expr(expr.Initializer, inLetExprBody, wStmts));
      wr.Write(")");
    }

    // Warning: NOT the same as NativeType.Bitwidth, which is zero except for
    // bitvector types
    private static int NativeTypeSize(NativeType nt) {
      switch (AsKotlinNativeType(nt)) {
        case KotlinNativeType.Byte: return 8;
        case KotlinNativeType.Short: return 16;
        case KotlinNativeType.Int: return 32;
        case KotlinNativeType.Long: return 64;
        default: Contract.Assert(false); throw new Cce.UnreachableException();
      }
    }

    protected override void EmitConversionExpr(Expression fromExpr, Type fromType, Type toType, bool inLetExprBody, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      if (fromType.IsNumericBased(Type.NumericPersuasion.Int) || fromType.NormalizeToAncestorType().IsBitVectorType || fromType.IsCharType) {
        if (toType.IsNumericBased(Type.NumericPersuasion.Real)) {
          // (int or bv or char) -> real
          Contract.Assert(AsNativeType(toType) == null);
          var fromNative = AsNativeType(fromType);
          wr.Write($"{DafnyBigRationalClass}(");
          if (fromNative != null) {
            if (fromNative.LowerBound >= 0) {
              wr.Write($"{DafnyHelpersClass}.unsignedToBigInteger");
              TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
            } else {
              wr.Write("dafny.BigInteger.valueOf(");
              TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
              wr.Write(".toLong())");
            }
            wr.Write(", dafny.BigInteger.ONE)");
          } else if (fromType.IsCharType) {
            wr.Append(Expr(fromExpr, inLetExprBody, wStmts));
            wr.Write(", 1)");
          } else {
            wr.Append(Expr(fromExpr, inLetExprBody, wStmts));
            wr.Write(", dafny.BigInteger.ONE)");
          }
        } else if (toType.IsCharType) {
          // (int or bv or char) -> char
          // Painfully, Java sign-extends bytes when casting to chars ...
          if (fromType.IsCharType) {
            EmitExpr(fromExpr, inLetExprBody, wr, wStmts);
          } else {
            var fromNative = AsNativeType(fromType);
            // In --unicode-char mode a char is represented as an Int code point; otherwise
            // as a Kotlin Char. Kotlin uses postfix .toX() conversions, not Java prefix casts.
            if (fromNative != null && fromNative.Sel == NativeType.Selection.Byte) {
              // Zero-extend the byte (Kotlin has no Byte.toUnsignedInt).
              wr.Write("(");
              TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
              wr.Write(".toInt() and 0xFF)");
              if (!UnicodeCharEnabled) {
                wr.Write(".toChar()");
              }
            } else if (UnicodeCharEnabled) {
              // char is an Int; toInt yields Int already.
              TrExprAsInt(fromExpr, wr, inLetExprBody, wStmts);
            } else {
              // char is a Kotlin Char.
              wr.Write("(");
              TrExprAsInt(fromExpr, wr, inLetExprBody, wStmts);
              wr.Write(").toChar()");
            }
          }
        } else {
          // (int or bv or char) -> (int or bv or ORDINAL)
          var fromNative = AsNativeType(fromType);
          var toNative = AsNativeType(toType);
          if (fromNative == null && toNative == null) {
            if (fromType.IsCharType) {
              // char -> big-integer (int or bv or ORDINAL). A char is an Int (unicode)
              // or Kotlin Char; BigInteger.valueOf takes a Long.
              wr.Write("dafny.BigInteger.valueOf((");
              if (UnicodeCharEnabled) {
                wr.Append(Expr(fromExpr, inLetExprBody, wStmts));
              } else {
                TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
                wr.Write(".code");
              }
              wr.Write(").toLong())");
            } else {
              // big-integer (int or bv) -> big-integer (int or bv or ORDINAL), so identity will do
              wr.Append(Expr(fromExpr, inLetExprBody, wStmts));
            }
          } else if (fromNative != null && toNative == null) {
            // native (int or bv) -> big-integer (int or bv)
            if (fromNative.LowerBound >= 0) {
              // unsignedToBigInteger has Byte/Short/Int/Long overloads.
              wr.Write($"{DafnyHelpersClass}.unsignedToBigInteger");
              TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
            } else {
              // Kotlin's BigInteger.valueOf only accepts Long.
              wr.Write("dafny.BigInteger.valueOf(");
              TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
              wr.Write(".toLong())");
            }
          } else if (fromNative != null && NativeTypeSize(toNative) == NativeTypeSize(fromNative)) {
            // native (int or bv) -> native (int or bv)
            // Cast between signed and unsigned, which have the same Java type
            TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
          } else {
            GetNativeInfo(toNative.Sel, out var toNativeName, out var toNativeSuffix, out var toNativeNeedsCast);
            // any (int or bv) -> native (int or bv)
            // A cast would do, but we also consider some optimizations
            var literal = PartiallyEvaluate(fromExpr);
            UnaryOpExpr u = fromExpr.Resolved as UnaryOpExpr;
            MemberSelectExpr m = fromExpr.Resolved as MemberSelectExpr;
            if (literal != null) {
              // Optimize constant to avoid intermediate BigInteger
              EmitNativeIntegerLiteral((BigInteger)literal, toNative, wr);
            } else if (u != null && u.Op == UnaryOpExpr.Opcode.Cardinality) {
              // Optimize || to avoid intermediate BigInteger
              wr.Write(CastIfSmallNativeType(toType));
              TrParenExpr("", u.E, wr, inLetExprBody, wStmts);
              wr.Write(".cardinalityInt()");
            } else if (m != null && m.MemberName == "Length" && m.Obj.Type.IsArrayType) {
              // Optimize .length to avoid intermediate BigInteger
              wr.Write(CastIfSmallNativeType(toType));
              var elmtType = UserDefinedType.ArrayElementType(m.Obj.Type);
              ConcreteSyntaxTree w;
              if (elmtType.IsTypeParameter) {
                wr.Write($"{FormatTypeDescriptorVariable(elmtType.AsTypeParameter)}.getArrayLength(");
                w = wr.Fork();
                wr.Write(")");
              } else {
                w = wr.Fork();
                wr.Write(".dim0");  // dafny.Array1<T> exposes dim0 (Int), not .length
              }
              TrParenExpr(m.Obj, w, inLetExprBody, wStmts);
            } else {
              // no optimization applies; use the standard translation
              if (fromNative != null && fromNative.LowerBound >= 0 && NativeTypeSize(fromNative) < NativeTypeSize(toNative)) {
                // Widening an unsigned value; careful!! Kotlin has no Byte.toUnsignedInt,
                // so zero-extend with a bit-mask: (x.toLong() and 0xFF) etc.
                var toLong = NativeTypeSize(toNative) == 64;
                var conv = toLong ? ".toLong()" : ".toInt()";
                // Mask off the sign-extended high bits introduced by toInt()/toLong().
                var fromBits = NativeTypeSize(fromNative);
                var mask = fromBits switch {
                  8 => toLong ? "0xFFL" : "0xFF",
                  16 => toLong ? "0xFFFFL" : "0xFFFF",
                  32 => "0xFFFFFFFFL",
                  _ => toLong ? "-1L" : "-1"
                };
                wr.Write("(");
                TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
                wr.Write($"{conv} and {mask})");
              } else {
                if (fromNative == null && !fromType.IsCharType) {
                  TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
                  wr.Write(NativeConversionMethod(toNative));
                } else {
                  wr.Write("(");
                  TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
                  wr.Write($"){NativeConversionMethod(toNative)}");
                }
              }
            }
          }
        }
      } else if (fromType.IsNumericBased(Type.NumericPersuasion.Real)) {
        Contract.Assert(AsNativeType(fromType) == null);
        if (toType.IsNumericBased(Type.NumericPersuasion.Real)) {
          // real -> real
          Contract.Assert(AsNativeType(toType) == null);
          wr.Append(Expr(fromExpr, inLetExprBody, wStmts));
        } else if (toType.IsCharType) {
          // real -> char: to the code point (Int), then .toChar() in non-unicode mode.
          wr.Write("(");
          TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
          wr.Write($".ToBigInteger().toInt(){(UnicodeCharEnabled ? "" : ".toChar()")})");
        } else if (toType.IsBigOrdinalType) {
          TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
          wr.Write(".ToBigInteger()");
        } else {
          // real -> (int or bv)
          TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
          wr.Write(".ToBigInteger()");
          if (AsNativeType(toType) != null) {
            wr.Write($"{NativeConversionMethod(AsNativeType(toType))}");
          }
        }
      } else if (fromType.IsBigOrdinalType) {
        if (toType.IsNumericBased(Type.NumericPersuasion.Int) || toType.IsCharType) {
          // ordinal -> int, char
          if (AsNativeType(toType) != null) {
            TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
            wr.Write($"{NativeConversionMethod(AsNativeType(toType))}");
          } else if (toType.IsCharType) {
            wr.Write("(");
            TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
            wr.Write($".toInt(){(UnicodeCharEnabled ? "" : ".toChar()")})");
          } else {
            TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
          }
        } else if (toType.IsNumericBased(Type.NumericPersuasion.Real)) {
          // ordinal -> real
          wr.Write($"{DafnyBigRationalClass}(");
          wr.Append(Expr(fromExpr, inLetExprBody, wStmts));
          wr.Write(", dafny.BigInteger.ONE)");
        } else if (toType.NormalizeToAncestorType().IsBitVectorType) {
          // ordinal -> bv
          if (AsNativeType(toType) != null) {
            TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
            wr.Write($"{NativeConversionMethod(AsNativeType(toType))}");
          } else {
            TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
          }
        } else if (toType.IsBigOrdinalType) {
          TrParenExpr(fromExpr, wr, inLetExprBody, wStmts);
        } else {
          Contract.Assert(false, $"not implemented for java: {fromType} -> {toType}");
        }
      } else if (fromType.Equals(toType) || fromType.AsNewtype != null || toType.AsNewtype != null) {
        wr.Append(Expr(fromExpr, inLetExprBody, wStmts));
      } else {
        wr = EmitDowncast(fromType, toType, fromExpr.Origin, wr);
        EmitExpr(fromExpr, inLetExprBody, wr, wStmts);
      }
    }

    protected override void EmitTypeTest(string localName, Type fromType, Type toType, IOrigin tok, ConcreteSyntaxTree wr) {
      // from T to U:   t is U && ...
      // from T to U?:  t is U && ...                 // since t is known to be non-null, this is fine
      // from T? to U:  t is U && ...                 // note, "is" implies non-null in Kotlin, so no need for explicit null check
      // from T? to U?: t == null || (t is U && ...)
      if (fromType.IsRefType && !fromType.IsNonNullRefType && toType.IsRefType && !toType.IsNonNullRefType) {
        wr = wr.Write($"{localName} == null || ").ForkInParens();
      }

      // Kotlin `is` can only check the erased type, so a generic target needs star
      // projection (`is TraitA<*>`); a bare `is TraitA` is a "one type argument expected"
      // error. Runtime type-arg checks aren't possible anyway (Dafny injectivity handles it).
      string typeName;
      if (toType.IsObject) {
        typeName = "Any";
      } else {
        var udt = (UserDefinedType)toType.NormalizeExpand();
        typeName = FullTypeName(udt);
        var nonGhostTypeArgs = SelectNonGhost(udt.ResolvedClass, udt.TypeArgs);
        if (nonGhostTypeArgs.Count > 0) {
          typeName += "<" + Util.Comma(nonGhostTypeArgs.Count, _ => "*") + ">";
        }
      }
      wr.Write($"{localName} is {typeName}");
    }

    protected override void EmitIsIntegerTest(Expression source, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      EmitExpr(source, false, wr.ForkInParens(), wStmts);
      wr.Write(".isInteger() && ");
    }

    protected override void EmitIsUnicodeScalarValueTest(Expression source, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      wr.Write("dafny.CodePoint.isCodePoint");
      EmitExpr(source, false, wr.ForkInParens(), wStmts);
      wr.Write(" && ");
    }

    protected override void EmitIsInIntegerRange(Expression source, BigInteger lo, BigInteger hi, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts) {
      EmitExpr(source, false, wr.ForkInParens(), wStmts);
      wr.Write(".compareTo(");
      EmitLiteralExpr(wr, new LiteralExpr(source.Origin, lo) { Type = Type.Int });
      wr.Write(") >= 0 && ");

      EmitExpr(source, false, wr.ForkInParens(), wStmts);
      wr.Write(".compareTo(");
      EmitLiteralExpr(wr, new LiteralExpr(source.Origin, hi) { Type = Type.Int });
      wr.Write(") < 0 && ");
    }

    protected override bool IssueCreateStaticMain(MethodOrConstructor m) {
      return true;
    }
    protected override ConcreteSyntaxTree CreateStaticMain(IClassWriter cw, string argsParameterName) {
      var wr = ((ClassWriter)cw).StaticMemberWriter;
      return wr.NewBlock($"fun __Main({argsParameterName}: dafny.DafnySequence<dafny.DafnySequence<{CharTypeName(true)}>>)");
    }

    protected override void CreateIIFE(string bvName, Type bvType, IOrigin bvTok, Type bodyType, IOrigin bodyTok,
      ConcreteSyntaxTree wr, ref ConcreteSyntaxTree wStmts, out ConcreteSyntaxTree wrRhs, out ConcreteSyntaxTree wrBody) {
      wr = EmitCoercionIfNecessary(NativeObjectType, bodyType, bvTok, wr);
      // TypeName/BoxedTypeName strip `?`, but the bound-variable's type must stay nullable
      // when bvType is a nullable Dafny ref type — otherwise a nullable RHS (e.g. a nullable
      // `is` operand) can't bind to the non-null Let<A, B> type argument / `val bv: A`.
      var bvNullable = bvType is { IsRefType: true, IsNonNullRefType: false };
      string Nq(string t) => bvNullable && !t.EndsWith("?") ? t + "?" : t;
      var boxedBvType = Nq(BoxedTypeName(bvType, wr, bvTok));
      // Kotlin: Helpers.Let<A, B>(rhs) { boxed -> val bv: A = boxed; body }
      // (Java emitted `Let<A,B>(rhs, boxed -> { A bv = boxed; return body; })`, whose
      // `boxed -> { ... }` lambda and `A bv =` declaration are not valid Kotlin.)
      wr.Write("{0}.Let<{1}, {2}>(", DafnyHelpersClass, boxedBvType, BoxedTypeName(bodyType, wr, bodyTok));
      wrRhs = wr.Fork();
      wrRhs = EmitCoercionIfNecessary(bvType, NativeObjectType, bvTok, wrRhs);

      var boxedBvName = idGenerator.FreshId("boxed");
      // Write the lambda body directly (no extra NewBlock — a nested `{ ... }` would make
      // the body a Function0 value instead of the returned expression). The lambda's last
      // expression is its result.
      wr.Write($") {{ {boxedBvName} -> ");
      wr.Write($"val {bvName}: {Nq(TypeName(bvType, wr, bvTok))} = ");
      var wrUnboxed = EmitCoercionIfNecessary(NativeObjectType, bvType, bvTok, wr.Fork());
      wrUnboxed.Write(boxedBvName);
      wr.Write("; ");
      wrBody = EmitCoercionIfNecessary(bodyType, NativeObjectType, bodyTok, wr.Fork());
      wr.Write(" }");
    }

    protected override string GetQuantifierName(string bvType) {
      // Kotlin uses native quantifier function in dafny package
      return "dafny.Helpers.quantifier";
    }

    // ABSTRACT METHOD DECLARATIONS FOR THE SAKE OF BUILDING PROGRAM

    protected override void EmitYield(ConcreteSyntaxTree wr) {
      throw new UnsupportedFeatureException(Token.NoToken, Feature.Iterators);
    }

    protected override ConcreteSyntaxTree CreateIterator(IteratorDecl iter, ConcreteSyntaxTree wr) {
      throw new UnsupportedFeatureException(iter.Origin, Feature.Iterators);
    }

    protected override void EmitHaltRecoveryStmt(Statement body, string haltMessageVarName, Statement recoveryBody, ConcreteSyntaxTree wr) {
      var tryBlock = wr.NewBlock("try");
      TrStmt(body, tryBlock);
      var catchBlock = wr.NewBlock("catch (e: dafny.DafnyHaltException)");
      var msgSeqType = $"dafny.DafnySequence<{CharTypeName(true)}>";
      var asStringCall = UnicodeCharEnabled ? "dafny.DafnySequence.asUnicodeString" : "dafny.DafnySequence.asString";
      // Trailing ';' terminates the declaration: otherwise the following `{ ... }`
      // recovery block would be parsed as a trailing lambda on the call above.
      catchBlock.WriteLine($"val {haltMessageVarName}: {msgSeqType} = {asStringCall}(e.message ?: \"\");");
      TrStmt(recoveryBody, catchBlock);
    }

    protected override void EmitNestedMatchExpr(NestedMatchExpr match, bool inLetExprBody, ConcreteSyntaxTree output,
      ConcreteSyntaxTree wStmts) {
      if (match.Cases.Count == 0) {
        base.EmitNestedMatchExpr(match, inLetExprBody, output, wStmts);
      } else {
        EmitExpr(match.Flattened, inLetExprBody, output, wStmts);
      }
    }

    protected override void TrOptNestedMatchExpr(NestedMatchExpr match, Type resultType, ConcreteSyntaxTree wr, ConcreteSyntaxTree wStmts,
      bool inLetExprBody, IVariable accumulatorVar, OptimizedExpressionContinuation continuation) {
      if (match.Cases.Count == 0) {
        base.TrOptNestedMatchExpr(match, resultType, wr, wStmts, inLetExprBody, accumulatorVar, continuation);
      } else {
        TrExprOpt(match.Flattened, resultType, wr, wStmts, inLetExprBody, accumulatorVar, continuation);
      }
    }

    protected override void EmitNestedMatchStmt(NestedMatchStmt match, ConcreteSyntaxTree writer) {
      if (match.Cases.Count == 0) {
        base.EmitNestedMatchStmt(match, writer);
      } else {
        TrStmt(match.Flattened, writer);
      }
    }
  }
}
