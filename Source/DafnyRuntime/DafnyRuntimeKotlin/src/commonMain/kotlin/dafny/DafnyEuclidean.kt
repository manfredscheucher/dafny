// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny


object DafnyEuclidean {
    // Properties of Euclidean Division, as referenced in post conditions
    // quotient >= 0 if sign(a) = sign(b) else quotient <= 0
    // remainder is always positive
    // a = quotient*b + remainder
    // there are no max values for these operations, but casting to unsigned is required if b is the MIN_VALUE of a
    // given type because there will be overflow. Since this is division, the return value for all methods will be
    // at a maximum the input value a, which is required to be well defined

    // pre: b != 0
    // post: quotient == a/b, as defined by Euclidean Division (http://en.wikipedia.org/wiki/Modulo_operation)
    fun EuclideanDivision(a: Byte, b: Byte): Byte {
        require(b.toInt() != 0) { "Precondition Failure" }
        return EuclideanDivision(a.toInt(), b.toInt()).toByte()
    }

    fun EuclideanDivision(a: Short, b: Short): Short {
        require(b.toInt() != 0) { "Precondition Failure" }
        return EuclideanDivision(a.toInt(), b.toInt()).toShort()
    }

    fun EuclideanDivision(a: Int, b: Int): Int {
        require(b != 0) { "Precondition Failure" }
        if (0 <= a) {
            return if (0 <= b) {
                // +a +b: a/b
                a / b
            } else {
                // +a -b: -(a/(-b))
                // if value of b is 0x80000000, then there is no positive representation for integers, so use uint
                if (b == Int.MIN_VALUE) {
                    // Kotlin-native equivalent of Integer.divideUnsigned(a, Int.MIN_VALUE)
                    (a.toUInt() / Int.MIN_VALUE.toUInt()).toInt() * -1
                } else {
                    -(a / -b)
                }
            }
        } else {
            return if (0 <= b) {
                // -a +b: -((-a-1)/b) - 1
                // minvalue check for a is not necessary because it will always be incremented one, and can
                // be represented in an int
                -((-(a + 1)) / b) - 1
            } else {
                // -a -b: ((-a-1)/(-b)) + 1
                if (b == Int.MIN_VALUE) {
                    // Kotlin-native equivalent of Integer.divideUnsigned(-(a + 1), Int.MIN_VALUE)
                    ((-(a + 1)).toUInt() / Int.MIN_VALUE.toUInt()).toInt() + 1
                } else {
                    (-(a + 1)) / (-b) + 1
                }
            }
        }
    }

    fun EuclideanDivision(a: Long, b: Long): Long {
        require(b != 0L) { "Precondition Failure" }
        if (0 <= a) {
            return if (0 <= b) {
                // +a +b: a/b
                a / b
            } else {
                // +a -b: -(a/(-b))
                // if value of b is 0x8000000000000000L, then there is no positive representation for longs,
                // so use ulong
                if (b == Long.MIN_VALUE) {
                    ((a).toULong() / (Long.MIN_VALUE).toULong()).toLong() * -1
                } else {
                    -(a / -b)
                }
            }
        } else {
            return if (0 <= b) {
                // -a +b: -((-a-1)/b) - 1
                // minvalue check for a is not necessary because it will always be incremented one, and can
                // be represented in a long
                -((-(a + 1)) / b) - 1
            } else {
                // -a -b: ((-a-1)/(-b)) + 1
                if (b == Long.MIN_VALUE) {
                    ((-(a + 1)).toULong() / (Long.MIN_VALUE).toULong()).toLong() + 1
                } else {
                    (-(a + 1)) / (-b) + 1
                }
            }
        }
    }

    fun EuclideanDivision(a: BigInteger, b: BigInteger): BigInteger {
        require(b.compareTo(BigInteger.ZERO) != 0) { "Precondition Failure" }
        if (0 <= a.signum()) {
            return if (0 <= b.signum()) {
                // +a +b: a/b
                a.divide(b)
            } else {
                // +a -b: -(a/(-b))
                a.divide(b.negate()).negate()
            }
        } else {
            return if (0 <= b.signum()) {
                // -a +b: -((-a-1)/b) - 1
                a.add(BigInteger.ONE).negate().divide(b).negate().subtract(BigInteger.ONE)
            } else {
                // -a -b: ((-a-1)/(-b)) + 1
                a.add(BigInteger.ONE).negate().divide(b.negate()).add(BigInteger.ONE)
            }
        }
    }

    // pre: b != 0
    // post: remainder == a%b, as defined by Euclidean Division (http://en.wikipedia.org/wiki/Modulo_operation)
    fun EuclideanModulus(a: Byte, b: Byte): Byte {
        require(b.toInt() != 0) { "Precondition Failure" }
        return EuclideanModulus(a.toInt(), b.toInt()).toByte()
    }

    fun EuclideanModulus(a: Short, b: Short): Short {
        require(b.toInt() != 0) { "Precondition Failure" }
        return EuclideanModulus(a.toInt(), b.toInt()).toShort()
    }

    fun EuclideanModulus(a: Int, b: Int): Int {
        require(b != 0) { "Precondition Failure" }
        if (0 <= a) {
            // +a: a % b'
            return if (b == Int.MIN_VALUE) {
                // Kotlin-native equivalent of Integer.remainderUnsigned(a, b)
                (a.toUInt() % b.toUInt()).toInt()
            } else if (b < 0) {
                a % -b
            } else {
                a % b
            }
        } else {
            // c = ((-a) % b')
            // -a: b' - c if c > 0
            // -a: 0 if c == 0
            return if (a == Int.MIN_VALUE || b == Int.MIN_VALUE) {
                if (a == b) {
                    0
                } else if (b == Int.MIN_VALUE) {
                    // Kotlin-native equivalent of Integer.remainderUnsigned(-a, b)
                    b - ((-a).toUInt() % b.toUInt()).toInt()
                } else {
                    val bp = if (b < 0) -b else b
                    // Kotlin-native equivalent of Integer.remainderUnsigned(a, bp)
                    bp - (a.toUInt() % bp.toUInt()).toInt()
                }
            } else {
                val bp = if (b < 0) -b else b
                val c = ((-a) % bp)
                if (c == 0) c else bp - c
            }
        }
    }

    fun EuclideanModulus(a: Long, b: Long): Long {
        require(b != 0L) { "Precondition Failure" }
        if (0 <= a) {
            // +a: a % b'
            return if (b == Long.MIN_VALUE) {
                ((a).toULong() % (b).toULong()).toLong()
            } else if (b < 0) {
                a % -b
            } else {
                a % b
            }
        } else {
            // c = ((-a) % b')
            // -a: b' - c if c > 0
            // -a: 0 if c == 0
            return if (a == Long.MIN_VALUE || b == Long.MIN_VALUE) {
                if (a == b) {
                    0
                } else if (b == Long.MIN_VALUE) {
                    b - ((-a).toULong() % (b).toULong()).toLong()
                } else {
                    val bp = if (b < 0) -b else b
                    bp - ((a).toULong() % (bp).toULong()).toLong()
                }
            } else {
                val bp = if (b < 0) -b else b
                val c = ((-a) % bp)
                if (c == 0L) c else bp - c
            }
        }
    }

    fun EuclideanModulus(a: BigInteger, b: BigInteger): BigInteger {
        require(b.compareTo(BigInteger.ZERO) != 0) { "Precondition Failure" }
        val bp = b.abs()
        return if (0 <= a.signum()) {
            // +a: a % b'
            a.mod(bp)
        } else {
            // c = ((-a) % b')
            // -a: b' - c if c > 0
            // -a: 0 if c == 0
            val c = a.negate().mod(bp)
            if (c == BigInteger.ZERO) c else bp.subtract(c)
        }
    }
}
