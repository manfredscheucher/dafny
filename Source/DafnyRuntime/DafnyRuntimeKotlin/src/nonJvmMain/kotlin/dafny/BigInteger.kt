// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

@file:Suppress("FunctionName")

package dafny

import com.ionspin.kotlin.bignum.integer.BigInteger as IonBigInteger
import com.ionspin.kotlin.bignum.integer.Sign

// Non-JVM `actual` for dafny.BigInteger — used by every Kotlin Multiplatform target that
// isn't the JVM (js, native, ...). Backed by the ionspin multiplatform bignum library, so
// no java. The expect API (java.math.BigInteger-compatible method names) is mapped onto
// ionspin's operators (plus/minus/…) and factory functions (fromInt/fromLong/parseString).
actual class BigInteger internal constructor(internal val value: IonBigInteger) : Comparable<BigInteger> {

    actual fun add(other: BigInteger): BigInteger = BigInteger(value + other.value)
    actual fun subtract(other: BigInteger): BigInteger = BigInteger(value - other.value)
    actual fun multiply(other: BigInteger): BigInteger = BigInteger(value * other.value)
    actual fun divide(other: BigInteger): BigInteger = BigInteger(value / other.value)
    actual fun remainder(other: BigInteger): BigInteger = BigInteger(value % other.value)
    actual fun mod(other: BigInteger): BigInteger = BigInteger(value.mod(other.value))
    actual fun negate(): BigInteger = BigInteger(value.negate())
    actual fun abs(): BigInteger = BigInteger(value.abs())
    actual fun gcd(other: BigInteger): BigInteger = BigInteger(value.gcd(other.value))
    actual fun min(other: BigInteger): BigInteger = if (value <= other.value) this else other
    actual fun max(other: BigInteger): BigInteger = if (value >= other.value) this else other
    actual fun pow(exponent: Int): BigInteger = BigInteger(value.pow(exponent))

    actual fun shiftLeft(n: Int): BigInteger = BigInteger(value.shl(n))
    actual fun shiftRight(n: Int): BigInteger = BigInteger(value.shr(n))
    actual fun and(other: BigInteger): BigInteger = BigInteger(value.and(other.value))
    actual fun or(other: BigInteger): BigInteger = BigInteger(value.or(other.value))
    actual fun xor(other: BigInteger): BigInteger = BigInteger(value.xor(other.value))
    actual fun not(): BigInteger = BigInteger(value.not())
    actual fun testBit(n: Int): Boolean = value.bitAt(n.toLong())
    actual fun bitLength(): Int = value.bitLength().toInt()

    actual override fun compareTo(other: BigInteger): Int = value.compareTo(other.value)
    actual fun signum(): Int = when (value.getSign()) {
        Sign.POSITIVE -> 1
        Sign.NEGATIVE -> -1
        Sign.ZERO -> 0
    }

    actual fun toByte(): Byte = value.intValue(exactRequired = false).toByte()
    actual fun toShort(): Short = value.intValue(exactRequired = false).toShort()
    actual fun toInt(): Int = value.intValue(exactRequired = false)
    actual fun toLong(): Long = value.longValue(exactRequired = false)
    actual fun intValueExact(): Int = value.intValue(exactRequired = true)
    actual fun longValueExact(): Long = value.longValue(exactRequired = true)
    actual fun intValue(): Int = value.intValue(exactRequired = false)
    actual fun longValue(): Long = value.longValue(exactRequired = false)

    actual override fun equals(other: Any?): Boolean = other is BigInteger && value == other.value
    actual override fun hashCode(): Int = value.hashCode()
    actual override fun toString(): String = value.toString()

    actual companion object {
        actual val ZERO: BigInteger = BigInteger(IonBigInteger.ZERO)
        actual val ONE: BigInteger = BigInteger(IonBigInteger.ONE)
        actual val TWO: BigInteger = BigInteger(IonBigInteger.TWO)
        actual val TEN: BigInteger = BigInteger(IonBigInteger.TEN)

        actual fun valueOf(v: Long): BigInteger = BigInteger(IonBigInteger.fromLong(v))
        actual fun valueOf(v: Int): BigInteger = BigInteger(IonBigInteger.fromInt(v))
        actual fun of(s: String): BigInteger = BigInteger(IonBigInteger.parseString(s, 10))
    }
}
