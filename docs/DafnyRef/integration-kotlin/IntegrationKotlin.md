---
title: Integrating Dafny and Kotlin code
---

## THIS FILE IS A WORK IN PROGRESS, IT CAN BE MODIFIED AT ANY TIME WITHOUT NOTICE

**The Dafny-to-Kotlin compilers are experimental and not officially supported
backends yet.**

The Kotlin backends translate Dafny to native Kotlin source (not Java bytecode)
so the output can target the JVM as well as the other Kotlin Multiplatform (KMP)
platforms.

There are three related targets:

| target        | output |
|---------------|--------|
| `kt`          | Kotlin for the JVM, as native Kotlin source |
| `kmp`         | a Kotlin Multiplatform Gradle project (JVM / JS / Native) |
| `jvm-ionspin` | a JVM artifact whose `BigInteger` is backed by the ionspin multiplatform-bignum library rather than `java.math.BigInteger` |

```
dafny translate kt  A.dfy --unicode-char:false
dafny translate kmp A.dfy --unicode-char:false
```

## Runtime

The generated code depends on the Kotlin runtime under
`Source/DafnyRuntime/DafnyRuntimeKotlin`, a Kotlin Multiplatform module:

- `commonMain` — the java-free core (sequences, sets, maps, multisets, tuples,
  function types, `BigRational`, code points, type descriptors);
- `jvmMain` — supplies `dafny.BigInteger` over `java.math.BigInteger`;
- `jsMain` / `nativeMain` / `nonJvmMain` — supply platform `exit` and, off the
  JVM, a `BigInteger` backed by the ionspin multiplatform-bignum library.

## BigInteger across platforms

Dafny's unbounded `int` maps to `dafny.BigInteger`. On the JVM this is
`java.math.BigInteger`; on JS and Native there is no such type, so the runtime
uses [ionspin/kotlin-multiplatform-bignum](https://github.com/ionspin/kotlin-multiplatform-bignum).
The `jvm-ionspin` target produces a JVM artifact that also uses the ionspin
implementation, so JVM output can be validated against the same bignum used on
the other platforms.

## Notes

- Pass `--unicode-char:false`; the runtime models Dafny `char` as a code point.
- The Kotlin backends are marked experimental (`IsStable = false`), so their
  `translate`/`build` subcommands are hidden from `--help` but fully usable.
- These backends are exercised out-of-tree by the `dafny-examples` project.
