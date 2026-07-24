using System.IO;

namespace Microsoft.Dafny.Compilers;

/// <summary>
/// The <c>jvm-ionspin</c> backend: a JVM-only Kotlin artifact (like <c>kt</c>), but whose
/// <c>dafny.BigInteger</c> is backed by the ionspin multiplatform bignum library instead of
/// <c>java.math.BigInteger</c> — so a JVM build uses the SAME bignum as a KMP js/native build
/// (uniform behavior across every target).
///
/// It reuses <see cref="KotlinMultiplatformBackend"/> in full: the same code generator, the same
/// Gradle project assembly, and the same JVM build/run path (<c>gradle jvmJar</c> / <c>runJvm</c>).
/// The ONLY difference is that <see cref="KotlinMultiplatformBackend.JvmUsesIonspin"/> is turned on,
/// which — during project assembly — wires the JVM source set to the ionspin <c>BigInteger</c>
/// actual (see <c>WireJvmToIonspin</c>). No generator or backend file is copied.
/// </summary>
public class JvmIonspinBackend : KotlinMultiplatformBackend {

  public JvmIonspinBackend(DafnyOptions options) : base(options) {
  }

  public override string TargetId => "jvm-ionspin";
  public override string TargetName => "Kotlin JVM (ionspin bignum)";

  public override string TargetBaseDir(string dafnyProgramName) =>
    $"{Path.GetFileNameWithoutExtension(dafnyProgramName)}-jvm-ionspin";

  protected override bool JvmUsesIonspin => true;
}
