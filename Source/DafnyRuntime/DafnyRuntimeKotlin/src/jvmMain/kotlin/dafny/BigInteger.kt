// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

@file:Suppress("FunctionName")
@file:JvmName("BigIntegerJvmKt")

package dafny

// JVM `actual` for dafny.BigInteger — the only place the runtime touches java.math.
// Other targets (js/native/wasm) provide their own actual backed by a multiplatform
// bignum library. `value` is internal so JVM-only interop spots (e.g. reflection-based
// array sizing in the JVM TypeDescriptor actual) can reach the underlying java value.
actual class BigInteger internal constructor(@JvmField internal val value: java.math.BigInteger) : Comparable<BigInteger> {

    actual fun add(other: BigInteger): BigInteger = BigInteger(value.add(other.value))
    actual fun subtract(other: BigInteger): BigInteger = BigInteger(value.subtract(other.value))
    actual fun multiply(other: BigInteger): BigInteger = BigInteger(value.multiply(other.value))
    actual fun divide(other: BigInteger): BigInteger = BigInteger(value.divide(other.value))
    actual fun remainder(other: BigInteger): BigInteger = BigInteger(value.remainder(other.value))
    actual fun mod(other: BigInteger): BigInteger = BigInteger(value.mod(other.value))
    actual fun negate(): BigInteger = BigInteger(value.negate())
    actual fun abs(): BigInteger = BigInteger(value.abs())
    actual fun gcd(other: BigInteger): BigInteger = BigInteger(value.gcd(other.value))
    actual fun min(other: BigInteger): BigInteger = BigInteger(value.min(other.value))
    actual fun max(other: BigInteger): BigInteger = BigInteger(value.max(other.value))
    actual fun pow(exponent: Int): BigInteger = BigInteger(value.pow(exponent))

    actual fun shiftLeft(n: Int): BigInteger = BigInteger(value.shiftLeft(n))
    actual fun shiftRight(n: Int): BigInteger = BigInteger(value.shiftRight(n))
    actual fun and(other: BigInteger): BigInteger = BigInteger(value.and(other.value))
    actual fun or(other: BigInteger): BigInteger = BigInteger(value.or(other.value))
    actual fun xor(other: BigInteger): BigInteger = BigInteger(value.xor(other.value))
    actual fun not(): BigInteger = BigInteger(value.not())
    actual fun testBit(n: Int): Boolean = value.testBit(n)
    actual fun bitLength(): Int = value.bitLength()

    actual override fun compareTo(other: BigInteger): Int = value.compareTo(other.value)
    actual fun signum(): Int = value.signum()

    actual fun toByte(): Byte = value.toByte()
    actual fun toShort(): Short = value.toShort()
    actual fun toInt(): Int = value.toInt()
    actual fun toLong(): Long = value.toLong()
    actual fun intValueExact(): Int = value.intValueExact()
    actual fun longValueExact(): Long = value.longValueExact()
    actual fun intValue(): Int = value.toInt()
    actual fun longValue(): Long = value.toLong()

    actual override fun equals(other: Any?): Boolean = other is BigInteger && value == other.value
    actual override fun hashCode(): Int = value.hashCode()
    actual override fun toString(): String = value.toString()

    actual companion object {
        actual val ZERO: BigInteger = BigInteger(java.math.BigInteger.ZERO)
        actual val ONE: BigInteger = BigInteger(java.math.BigInteger.ONE)
        actual val TWO: BigInteger = BigInteger(java.math.BigInteger.TWO)
        actual val TEN: BigInteger = BigInteger(java.math.BigInteger.TEN)

        @JvmStatic actual fun valueOf(v: Long): BigInteger = BigInteger(java.math.BigInteger.valueOf(v))
        @JvmStatic actual fun valueOf(v: Int): BigInteger = BigInteger(java.math.BigInteger.valueOf(v.toLong()))
        @JvmStatic actual fun of(s: String): BigInteger = BigInteger(java.math.BigInteger(s))
    }
}

// JVM-only interop helpers (not part of the common expect API): wrap/unwrap the
// underlying java.math.BigInteger for the few JVM reflection spots.
internal fun BigInteger.toJava(): java.math.BigInteger = this.value
internal fun bigIntegerFromJava(v: java.math.BigInteger): BigInteger = BigInteger(v)
