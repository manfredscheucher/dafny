// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

@file:Suppress("FunctionName")

package dafny

// Platform-independent arbitrary-precision integer for the Dafny Kotlin runtime.
//
// This is the SINGLE big-integer abstraction the runtime and generated code depend on.
// The `expect` declaration here is java-free; the JVM `actual` (jvmMain) delegates to
// java.math.BigInteger, and other Kotlin Multiplatform targets (js/native/wasm) can
// supply an `actual` backed by a multiplatform bignum library (e.g. ionspin
// kotlin-multiplatform-bignum). No consumer references java.math.* directly.
//
// The API mirrors the java.math.BigInteger method names (add/subtract/multiply/…,
// valueOf, ZERO/ONE/…) so existing runtime and generated code only needs to import
// `dafny.BigInteger`. Kotlin operator overloads are declared on top for idiomatic use.
expect class BigInteger : Comparable<BigInteger> {

    // arithmetic (java.math.BigInteger-compatible names)
    fun add(other: BigInteger): BigInteger
    fun subtract(other: BigInteger): BigInteger
    fun multiply(other: BigInteger): BigInteger
    fun divide(other: BigInteger): BigInteger
    fun remainder(other: BigInteger): BigInteger
    fun mod(other: BigInteger): BigInteger
    fun negate(): BigInteger
    fun abs(): BigInteger
    fun gcd(other: BigInteger): BigInteger
    fun min(other: BigInteger): BigInteger
    fun max(other: BigInteger): BigInteger
    fun pow(exponent: Int): BigInteger

    // bit operations
    fun shiftLeft(n: Int): BigInteger
    fun shiftRight(n: Int): BigInteger
    fun and(other: BigInteger): BigInteger
    fun or(other: BigInteger): BigInteger
    fun xor(other: BigInteger): BigInteger
    fun not(): BigInteger
    fun testBit(n: Int): Boolean
    fun bitLength(): Int

    // comparison / sign
    override fun compareTo(other: BigInteger): Int
    fun signum(): Int

    // conversions
    fun toByte(): Byte
    fun toShort(): Short
    fun toInt(): Int
    fun toLong(): Long
    fun intValueExact(): Int
    fun longValueExact(): Long
    fun intValue(): Int
    fun longValue(): Long

    override fun equals(other: Any?): Boolean
    override fun hashCode(): Int
    override fun toString(): String

    companion object {
        val ZERO: BigInteger
        val ONE: BigInteger
        val TWO: BigInteger
        val TEN: BigInteger

        fun valueOf(v: Long): BigInteger
        fun valueOf(v: Int): BigInteger
        fun of(s: String): BigInteger
    }
}

// Kotlin operator overloads — declared in common so all targets share them.
operator fun BigInteger.plus(other: BigInteger): BigInteger = add(other)
operator fun BigInteger.minus(other: BigInteger): BigInteger = subtract(other)
operator fun BigInteger.times(other: BigInteger): BigInteger = multiply(other)
operator fun BigInteger.div(other: BigInteger): BigInteger = divide(other)
operator fun BigInteger.rem(other: BigInteger): BigInteger = remainder(other)
operator fun BigInteger.unaryMinus(): BigInteger = negate()
