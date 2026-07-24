using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Dafny.Compilers;

/// <summary>
/// The Kotlin Multiplatform backend (<c>--target:kmp</c>).
///
/// It reuses the exact same code generator as the JVM Kotlin backend — the generated code is
/// platform-independent (no java.*) — but instead of compiling a JVM jar with kotlinc, it emits a
/// complete Kotlin Multiplatform Gradle project: the java-free runtime source tree (commonMain +
/// jvmMain + nonJvmMain/jsMain/nativeMain), the generated Dafny code, and a build.gradle.kts wiring
/// up jvm/js/native targets (ionspin bignum for the non-JVM targets). The project can then be built
/// or run for any target via Gradle.
/// </summary>
public class KotlinMultiplatformBackend : KotlinBackend {

  public KotlinMultiplatformBackend(DafnyOptions options) : base(options) {
  }

  public override string TargetId => "kmp";
  public override string TargetName => "Kotlin Multiplatform";
  // TargetExtension ("kt") and IsStable (false) are inherited unchanged from KotlinBackend.

  public override string TargetBaseDir(string dafnyProgramName) =>
    $"{Path.GetFileNameWithoutExtension(dafnyProgramName)}-kmp";

  /// <summary>
  /// When true, the assembled project's JVM sub-target is wired to use the ionspin bignum
  /// (the nonJvmMain <c>dafny.BigInteger</c> actual) instead of its own <c>java.math.BigInteger</c>
  /// actual, so a JVM build behaves exactly like a KMP js/native build. Overridden by
  /// <c>JvmIonspinBackend</c> (the <c>jvm-ionspin</c> target). Kept <c>false</c> for the plain
  /// <c>kmp</c> target, whose JVM output must keep using java.math.
  /// </summary>
  protected virtual bool JvmUsesIonspin => false;

  private const string RuntimeSourcesResource = "DafnyRuntimeKotlinSources.zip";
  private const string GradleCommand = "gradle";

  private static string Gradle =>
    Environment.GetEnvironmentVariable("DAFNY_GRADLE") ?? GradleCommand;

  /// <summary>
  /// Assemble a Kotlin Multiplatform Gradle project under the target directory:
  ///   &lt;proj&gt;/settings.gradle.kts, build.gradle.kts
  ///   &lt;proj&gt;/src/commonMain/kotlin/...      (runtime commonMain + generated Dafny code)
  ///   &lt;proj&gt;/src/jvmMain|nonJvmMain|jsMain|nativeMain/kotlin/...   (runtime actuals)
  /// The generated code (Main.kt + _System/*) already lives under the target dir from translation;
  /// we move it into src/commonMain/kotlin so it shares the common source set with the runtime.
  /// </summary>
  public override async Task<(bool Success, object CompilationResult)> CompileTargetProgram(
    string dafnyProgramName,
    string targetProgramText,
    string callToMain /*?*/, string targetFilename /*?*/,
    ReadOnlyCollection<string> otherFileNames, bool runAfterCompile, IDafnyOutputWriter outputWriter) {

    var targetDirectory = Path.GetFullPath(Path.GetDirectoryName(targetFilename));

    string projectDir;
    try {
      projectDir = AssembleKmpProject(targetDirectory, otherFileNames);
    } catch (Exception e) {
      await outputWriter.Status($"Error while assembling the Kotlin Multiplatform project: {e.Message}");
      return (false, null);
    }

    // Build the JVM jar of the whole project (common + generated code + runtime) so that
    // `dafny run --target:kmp` can execute it, and `dafny build` leaves a runnable artifact.
    // js/native are available in the same project via `gradle jsNodeRun` /
    // `gradle linkReleaseExecutable<Target>` etc.
    var args = new List<string> { "-q", "jvmJar" };
    var buildProcess = PrepareProcessStartInfo(Gradle, args);
    buildProcess.WorkingDirectory = projectDir;

    await using var sw = outputWriter.StatusWriter();
    if (0 != await RunProcess(buildProcess, sw, sw, "Error while building the Kotlin Multiplatform project with Gradle.")) {
      return (false, null);
    }

    if (Options.Verbose) {
      await outputWriter.Status($"Wrote Kotlin Multiplatform project to {projectDir}");
    }

    return (true, projectDir);
  }

  public override async Task<bool> RunTargetProgram(
    string dafnyProgramName, string targetProgramText,
    string callToMain,
    string targetFilename /*?*/,
    ReadOnlyCollection<string> otherFileNames, object compilationResult,
    IDafnyOutputWriter outputWriter) {

    var projectDir = compilationResult as string
      ?? Path.Combine(Path.GetFullPath(Path.GetDirectoryName(targetFilename) ?? "."));

    // Run the generated program on the JVM target via Gradle. (js/native runs use the same
    // project's jsNodeRun / run<Target>ReleaseExecutable tasks.)
    var args = new List<string> { "-q", "runJvm" };
    args.AddRange(Options.MainArgs);
    var psi = PrepareProcessStartInfo(Gradle, args);
    psi.WorkingDirectory = projectDir;

    await using var sw = outputWriter.StatusWriter();
    await using var ew = outputWriter.ErrorWriter();
    return 0 == await RunProcess(psi, sw, ew);
  }

  /// <summary>
  /// Unpack the embedded runtime source zip and lay out the KMP project. Returns the project dir.
  /// </summary>
  private string AssembleKmpProject(string targetDirectory, ReadOnlyCollection<string> otherFileNames) {
    var projectDir = targetDirectory;
    var srcCommon = Path.Combine(projectDir, "src", "commonMain", "kotlin");

    // 1. Unpack the runtime source tree (creates src/{commonMain,jvmMain,nonJvmMain,...} +
    //    build.gradle.kts + settings.gradle.kts) into the project dir.
    ExtractRuntimeSources(projectDir);

    // 2. Move the generated Dafny code (Main.kt + _System/ + any module dirs) into
    //    src/commonMain/kotlin so it shares the common source set with the runtime.
    Directory.CreateDirectory(srcCommon);
    foreach (var entry in Directory.EnumerateFileSystemEntries(projectDir)) {
      var name = Path.GetFileName(entry);
      // Skip the project scaffolding we just unpacked / are creating, and non-Kotlin
      // artifacts (the .dtr translation record, the extracted runtime jar).
      if (name is "src" or "build" or "settings.gradle.kts" or "build.gradle.kts"
          or "gradle" or "gradlew" or "gradlew.bat" or ".gradle" or ".kotlin") {
        continue;
      }
      if (File.Exists(entry) && Path.GetExtension(entry) is ".dtr" or ".jar") {
        continue;
      }
      var dest = Path.Combine(srcCommon, name);
      if (Directory.Exists(entry)) {
        MoveDirectoryMerging(entry, dest);
      } else if (File.Exists(entry)) {
        Directory.CreateDirectory(srcCommon);
        File.Copy(entry, dest, true);
        File.Delete(entry);
      }
    }

    // 3. Copy any extra user .kt files (externs) into commonMain as well.
    foreach (var otherFileName in otherFileNames.Where(f => Path.GetExtension(f) == ".kt")) {
      File.Copy(otherFileName, Path.Combine(srcCommon, Path.GetFileName(otherFileName)), true);
    }

    // 4. Rewrite build.gradle.kts so the runtime is a runnable application (add a JVM `application`
    //    entry point + convenience run tasks) rather than a bare library.
    PatchBuildGradleForRun(Path.Combine(projectDir, "build.gradle.kts"));

    // 5. For the `jvm-ionspin` target: wire the JVM sub-target to the ionspin bignum so the JVM
    //    build behaves exactly like a KMP js/native build. Applied only to the assembled project;
    //    the shared runtime source on disk is never modified.
    if (JvmUsesIonspin) {
      WireJvmToIonspin(projectDir);
    }

    return projectDir;
  }

  /// <summary>
  /// Make the assembled project's <c>jvmMain</c> source set use the ionspin <c>dafny.BigInteger</c>
  /// actual (from <c>nonJvmMain</c>) instead of its own <c>java.math.BigInteger</c> actual:
  ///   (1) in build.gradle.kts, change <c>val jvmMain by getting</c> to
  ///       <c>val jvmMain by getting { dependsOn(nonJvmMain) }</c> so jvmMain inherits the ionspin
  ///       actual and the ionspin dependency;
  ///   (2) delete <c>src/jvmMain/kotlin/dafny/BigInteger.kt</c> (the java.math actual), otherwise the
  ///       JVM compile path has two <c>actual class BigInteger</c> (duplicate-actual error).
  /// <c>src/jvmMain/kotlin/dafny/PlatformExit.kt</c> is kept (it is ionspin-independent).
  /// </summary>
  private static void WireJvmToIonspin(string projectDir) {
    var buildGradlePath = Path.Combine(projectDir, "build.gradle.kts");
    if (File.Exists(buildGradlePath)) {
      var text = File.ReadAllText(buildGradlePath);
      if (!text.Contains("// dafny-jvm-ionspin")) {
        // The runtime declares `val jvmMain by getting` with no configuration block; give it the
        // nonJvmMain dependency so it picks up the ionspin BigInteger actual + the ionspin library.
        text = text.Replace(
          "val jvmMain by getting\n",
          "val jvmMain by getting { dependsOn(nonJvmMain) } // dafny-jvm-ionspin\n");
        File.WriteAllText(buildGradlePath, text);
      }
    }

    // Remove the java.math actual so it does not collide with the ionspin actual on the JVM path.
    var jvmJavaMathBigInteger =
      Path.Combine(projectDir, "src", "jvmMain", "kotlin", "dafny", "BigInteger.kt");
    if (File.Exists(jvmJavaMathBigInteger)) {
      File.Delete(jvmJavaMathBigInteger);
    }
  }

  private void ExtractRuntimeSources(string projectDir) {
    var assembly = System.Reflection.Assembly.Load("DafnyPipeline");
    using var stream = assembly.GetManifestResourceStream(RuntimeSourcesResource);
    if (stream == null) {
      throw new Exception($"Cannot find embedded resource: {RuntimeSourcesResource}");
    }
    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
    foreach (var e in archive.Entries) {
      if (string.IsNullOrEmpty(e.Name)) {
        continue; // directory entry
      }
      var dest = Path.GetFullPath(Path.Combine(projectDir, e.FullName));
      Directory.CreateDirectory(Path.GetDirectoryName(dest));
      using var es = e.Open();
      using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write);
      es.CopyTo(fs);
    }
  }

  private static void MoveDirectoryMerging(string source, string dest) {
    Directory.CreateDirectory(dest);
    foreach (var file in Directory.EnumerateFiles(source)) {
      File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
    }
    foreach (var dir in Directory.EnumerateDirectories(source)) {
      MoveDirectoryMerging(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
    Directory.Delete(source, true);
  }

  /// <summary>
  /// Turn the runtime's build.gradle.kts (a library) into a runnable application for the
  /// generated Dafny program: the generated `fun main` in commonMain becomes each target's
  /// entry point. Adds a `runJvm` task (JVM), makes the JS target an executable (so
  /// `jsNodeRun` works), and makes the native targets executables with `entryPoint = "main"`
  /// (so `run<Target>ReleaseExecutable` / `Debug` work).
  /// </summary>
  private static void PatchBuildGradleForRun(string buildGradlePath) {
    if (!File.Exists(buildGradlePath)) {
      return;
    }
    var text = File.ReadAllText(buildGradlePath);

    if (text.Contains("// dafny-kmp-run")) {
      return; // already patched
    }

    // JS: library -> executable so `gradle jsNodeRun` runs the generated main.
    text = text.Replace("binaries.library()", "binaries.executable()");

    // Native: plain target -> executable target with the generated `fun main` as entry point.
    foreach (var t in new[] { "macosArm64", "macosX64", "linuxX64", "mingwX64" }) {
      text = text.Replace($"    {t}()",
        $"    {t} {{ binaries {{ executable {{ entryPoint = \"main\" }} }} }}");
    }

    // JVM: a JavaExec run task against the jvm compilation output.
    const string runTasks = @"

// dafny-kmp-run: make the generated `fun main` runnable on the JVM.
tasks.register<JavaExec>(""runJvm"") {
    group = ""application""
    dependsOn(""jvmMainClasses"")
    val jvmMain = kotlin.jvm().compilations.getByName(""main"")
    classpath = jvmMain.output.allOutputs + jvmMain.runtimeDependencyFiles
    mainClass.set(""MainKt"")
}
";
    text += runTasks;
    File.WriteAllText(buildGradlePath, text);
  }
}
