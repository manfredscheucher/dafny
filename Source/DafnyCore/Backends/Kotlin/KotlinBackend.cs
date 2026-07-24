using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.CommandLine;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DafnyCore.Options;

namespace Microsoft.Dafny.Compilers;

public class KotlinBackend : ExecutableBackend {

  public static readonly Option<bool> LegacyDataConstructors = new("--legacy-data-constructors",
    "Enables legacy data constructor generation for Kotlin (default: false)");

  static KotlinBackend() {
    DafnyOptions.RegisterLegacyUi(LegacyDataConstructors, DafnyOptions.ParseBoolean, "Compilation options", legacyName: "legacyDataConstructors", defaultValue: false);
    OptionRegistry.RegisterOption(LegacyDataConstructors, OptionScope.Cli);
  }

  public override IEnumerable<Option> SupportedOptions => new List<Option> { LegacyDataConstructors };

  public override IReadOnlySet<string> SupportedExtensions => new HashSet<string> { ".kt" };

  public override string TargetName => "Kotlin";
  public override bool IsStable => false;
  public override string TargetExtension => "kt";

  public override string TargetBaseDir(string dafnyProgramName) =>
    $"{Path.GetFileNameWithoutExtension(dafnyProgramName)}-kotlin";

  public override string TargetBasename(string dafnyProgramName) => "Main";

  public override bool SupportsInMemoryCompilation => false;
  public override bool TextualTargetIsExecutable => true;

  // The generated entry point is a top-level `main` in Main.kt, which kotlinc compiles to
  // the JVM class `MainKt`.
  private const string MainClassName = "MainKt";
  private const string RuntimeJarName = "DafnyRuntimeKotlin.jar";

  public override IReadOnlySet<string> SupportedNativeTypes =>
    new HashSet<string> { "byte", "sbyte", "ushort", "short", "uint", "int", "number", "ulong", "long" };

  protected override SinglePassCodeGenerator CreateCodeGenerator() {
    return new KotlinCodeGenerator(Options, Reporter);
  }

  public override void CleanSourceDirectory(string sourceDirectory) {
    try {
      Directory.Delete(sourceDirectory, true);
    } catch (DirectoryNotFoundException) {
    }
  }

  /// <summary>
  /// Extract the embedded Kotlin runtime jar (which bundles the Kotlin stdlib) into the
  /// given directory and return its full path. Mirrors JavaBackend.EmitRuntimeJar.
  /// </summary>
  private string EmitRuntimeJar(string targetDirectory) {
    var assembly = System.Reflection.Assembly.Load("DafnyPipeline");
    var stream = assembly.GetManifestResourceStream(RuntimeJarName);
    if (stream == null) {
      throw new Exception($"Cannot find embedded resource: {RuntimeJarName}");
    }

    var fullJarName = Path.Combine(targetDirectory, RuntimeJarName);
    using (var outStream = new FileStream(fullJarName, FileMode.Create, FileAccess.Write)) {
      stream.CopyTo(outStream);
    }
    return fullJarName;
  }

  public override async Task<(bool Success, object CompilationResult)> CompileTargetProgram(
    string dafnyProgramName,
    string targetProgramText,
    string callToMain /*?*/, string targetFilename /*?*/,
    ReadOnlyCollection<string> otherFileNames, bool runAfterCompile, IDafnyOutputWriter outputWriter) {

    foreach (var otherFileName in otherFileNames) {
      if (Path.GetExtension(otherFileName) != ".kt") {
        await outputWriter.Status($"Unrecognized file as extra input for Kotlin compilation: {otherFileName}");
        return (false, null);
      }
      if (!await CopyExternLibraryIntoPlace(externFilename: otherFileName, mainProgram: targetFilename, outputWriter: outputWriter)) {
        return (false, null);
      }
    }

    var targetDirectory = Path.GetFullPath(Path.GetDirectoryName(targetFilename));

    // Extract the Kotlin runtime jar next to the generated sources so kotlinc can use it.
    string runtimeJar;
    try {
      runtimeJar = EmitRuntimeJar(targetDirectory);
    } catch (Exception e) {
      await outputWriter.Status($"Error while extracting the Kotlin runtime: {e.Message}");
      return (false, null);
    }

    // Collect all generated .kt files (recursively, like the Java backend collects .java).
    var sourceFiles = Directory.EnumerateFiles(targetDirectory, "*.kt", SearchOption.AllDirectories)
      .Select(Path.GetFullPath)
      .ToList();
    if (sourceFiles.Count == 0) {
      await outputWriter.Status("No Kotlin source files were generated.");
      return (false, null);
    }

    var jarPath = Path.GetFullPath(Path.ChangeExtension(dafnyProgramName, ".jar"));
    Directory.CreateDirectory(Path.GetDirectoryName(jarPath));

    // Compile all generated .kt files into a single self-contained jar.
    // -include-runtime bundles the Kotlin stdlib so the resulting jar is runnable with plain `java -jar`.
    var args = new List<string> {
      "-classpath", runtimeJar,
      "-include-runtime",
      "-d", jarPath,
    };
    args.AddRange(sourceFiles);

    var compileProcess = PrepareProcessStartInfo(KotlincCommand, args);
    compileProcess.WorkingDirectory = targetDirectory;

    await using var sw = outputWriter.StatusWriter();
    if (0 != await RunProcess(compileProcess, sw, sw, "Error while compiling Kotlin files.")) {
      return (false, null);
    }

    // Keep the runtime jar around next to the output jar so RunTargetProgram can put it on the
    // classpath (the kotlin stdlib is bundled into our jar via -include-runtime, but extern
    // helpers may still reference it). Place a copy beside the output jar.
    var runtimeBesideOutput = Path.Combine(Path.GetDirectoryName(jarPath), RuntimeJarName);
    try {
      if (!string.Equals(Path.GetFullPath(runtimeBesideOutput), Path.GetFullPath(runtimeJar), StringComparison.Ordinal)) {
        File.Copy(runtimeJar, runtimeBesideOutput, true);
      }
    } catch {
      // Non-fatal: the stdlib is already bundled into the output jar.
    }

    if (Options.UsingNewCli && Options.SpillTargetCode == 0) {
      try {
        Directory.Delete(targetDirectory, true);
      } catch {
        // ignore
      }
    }

    if (Options.Verbose) {
      var fileKind = callToMain != null ? "executable" : "library";
      await outputWriter.Status($"Wrote {fileKind} jar {Path.GetFileName(jarPath)}");
    }

    return (true, null);
  }

  private async Task<bool> CopyExternLibraryIntoPlace(string externFilename, string mainProgram, IDafnyOutputWriter outputWriter) {
    var mainDir = Path.GetDirectoryName(mainProgram);
    Contract.Assert(mainDir != null);
    var tgtFilename = Path.Combine(mainDir, Path.GetFileName(externFilename));
    Directory.CreateDirectory(mainDir);
    FileInfo file = new FileInfo(externFilename);
    file.CopyTo(tgtFilename, true);
    if (Options.Verbose) {
      await outputWriter.Status($"Additional input {externFilename} copied to {tgtFilename}");
    }
    return true;
  }

  public override async Task<bool> RunTargetProgram(
    string dafnyProgramName, string targetProgramText,
    string callToMain,
    string targetFilename /*?*/,
    ReadOnlyCollection<string> otherFileNames, object compilationResult,
    IDafnyOutputWriter outputWriter) {

    var jarPath = Path.GetFullPath(Path.ChangeExtension(dafnyProgramName, ".jar")); // Must match CompileTargetProgram
    var runtimeJar = Path.Combine(Path.GetDirectoryName(jarPath), RuntimeJarName);

    // The output jar is self-contained (Kotlin stdlib bundled), but we add the runtime jar to the
    // classpath as well so that anything depending on the dafny runtime classes resolves.
    var classpath = File.Exists(runtimeJar)
      ? string.Join(Path.PathSeparator.ToString(), jarPath, runtimeJar)
      : jarPath;

    var args = new List<string> {
      "-Dfile.encoding=UTF-8",
      "-classpath", classpath,
      MainClassName,
    };
    args.AddRange(Options.MainArgs);

    var psi = PrepareProcessStartInfo(JavaCommand, args);

    await using var sw = outputWriter.StatusWriter();
    await using var ew = outputWriter.ErrorWriter();
    return 0 == await RunProcess(psi, sw, ew);
  }

  // Allow overriding the kotlinc/java executables via environment variables, otherwise rely on PATH
  // (mirrors how the Java backend relies on `javac`/`java` being on PATH).
  private static string KotlincCommand =>
    Environment.GetEnvironmentVariable("DAFNY_KOTLINC") ?? "kotlinc";

  private static string JavaCommand =>
    Environment.GetEnvironmentVariable("DAFNY_JAVA") ?? "java";

  public KotlinBackend(DafnyOptions options) : base(options) {
  }
}
