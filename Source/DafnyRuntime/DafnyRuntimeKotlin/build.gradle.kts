// Kotlin Multiplatform build for the Dafny Kotlin runtime.
//
// commonMain is java-free (the platform-independent runtime). Each target supplies the
// `actual` for dafny.BigInteger: jvmMain wraps java.math.BigInteger; all non-JVM targets
// (js, native) share a single nonJvmMain actual backed by the ionspin multiplatform bignum
// library. Everything else is shared, unmodified common code.

plugins {
    kotlin("multiplatform") version "2.1.0"
}

group = "dafny"
version = "1.0"

repositories {
    mavenCentral()
}

kotlin {
    jvm {
        compilations.all {
            kotlinOptions.jvmTarget = "17"
        }
    }
    js(IR) {
        nodejs()
        binaries.library()
    }
    // Native targets (host-appropriate ones build locally; all are declared for CI).
    // The runtime is a library; the generated Dafny program supplies `fun main` and links
    // against it. (Verified end-to-end: a generated program built as an executable against
    // these targets runs correctly on js/node and native — see git history.)
    macosArm64()
    macosX64()
    linuxX64()
    mingwX64()

    applyDefaultHierarchyTemplate()

    sourceSets {
        val commonMain by getting

        // Shared source set for every non-JVM target: the ionspin-backed BigInteger actual.
        val nonJvmMain by creating {
            dependsOn(commonMain)
            dependencies {
                implementation("com.ionspin.kotlin:bignum:0.3.10")
            }
        }

        val jvmMain by getting

        val jsMain by getting { dependsOn(nonJvmMain) }
        val nativeMain by getting { dependsOn(nonJvmMain) }
    }
}
