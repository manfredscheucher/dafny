// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny


class BigRational {
    
    var num: BigInteger // invariant 1 <= den || (num == 0 && den == 0)
    
    var den: BigInteger

    constructor() : this(0, 1)

    constructor(n: Int) : this(n, 1)

    constructor(n: Int, d: Int) : this(BigInteger.valueOf(n.toLong()), BigInteger.valueOf(d.toLong()))

    constructor(n: BigInteger, d: BigInteger) {
        require(d != BigInteger.ZERO) { "Precondition Failure" }
        // ensures 1 <= den
        if (d.compareTo(BigInteger.ZERO) < 0) {
            num = n.negate()
            den = d.negate()
        } else {
            num = n
            den = d
        }
    }

    override fun toString(): String {
        if (den == BigInteger.ONE || num == BigInteger.ZERO) {
            return "$num.0"
        } else {
            val t = dividesAPowerOf10(den)
            val log10 = t.dtor__2()
            if (t.dtor__0()) {
                val n = num.multiply(t.dtor__1())
                val sign: String
                val digits: String
                if (num.signum() < 0) {
                    sign = "-"
                    digits = (n.negate()).toString()
                } else {
                    sign = ""
                    digits = n.toString()
                }

                return if (log10 < digits.length) {
                    val digitCount = digits.length - log10
                    sign + digits.substring(0, digitCount) + "." + digits.substring(digitCount)
                } else {
                    val z = log10 - digits.length
                    val outputBuffer = StringBuilder()
                    for (i in 0 until z) {
                        outputBuffer.append("0")
                    }
                    sign + "0." + outputBuffer.toString() + digits
                }
            } else {
                return "($num.0 / $den.0)"
            }
        }
    }

    fun ToBigInteger(): BigInteger {
        return if (num == BigInteger.ZERO || den == BigInteger.ONE) {
            num
        } else if (0 < num.signum()) {
            num.divide(den)
        } else {
            // Dafny uses Euclidean Division, so divide will not always satisfy the preconditions of
            // num = (den * BigInteger) + remainder
            // when the numerator is negative.
            (num.subtract(den).add(BigInteger.ONE)).divide(den)
        }
    }

    fun isInteger(): Boolean {
        val floored = BigRational(this.ToBigInteger(), BigInteger.ONE)
        return this == floored
    }

    fun reduce(): BigRational {
        val gcd = num.abs().gcd(den)
        if (gcd == BigInteger.ONE) return this
        return BigRational(num.divide(gcd), den.divide(gcd))
    }

    fun compareTo(that: BigRational): Int {
        // simple things first
        val asign = this.num.signum()
        val bsign = that.num.signum()
        if (asign < 0 && 0 <= bsign) {
            return -1
        } else if (asign <= 0 && 0 < bsign) {
            return -1
        } else if (bsign < 0 && 0 <= asign) {
            return 1
        } else if (bsign <= 0 && 0 < asign) {
            return 1
        }

        val n = Normalize(this, that)
        return n.dtor__0().compareTo(n.dtor__1())
    }

    fun signum(): Int {
        return this.num.signum()
    }

    override fun hashCode(): Int {
        return num.hashCode() + 29 * den.hashCode()
    }

    override fun equals(obj: Any?): Boolean {
        if (this === obj) return true
        if (obj == null) return false
        if (this::class != obj::class) return false
        val o = obj as BigRational
        val t = Normalize(this, o)
        return t.dtor__0() == t.dtor__1()
    }

    fun add(b: BigRational): BigRational {
        return add(this, b)
    }

    fun subtract(b: BigRational): BigRational {
        return subtract(this, b)
    }

    fun negate(): BigRational {
        return BigRational(num.negate(), den)
    }

    fun multiply(b: BigRational): BigRational {
        return multiply(this, b)
    }

    fun divide(b: BigRational): BigRational {
        return divide(this, b)
    }

    companion object {
        // TODO: Implement default method, and disallow 0 for den
        
        val ZERO = BigRational(0)

        fun isPowerOf10(x: BigInteger): Tuple2<Boolean, Int> {
            var x = x
            val log10 = 0
            if (x == BigInteger.ZERO) {
                return Tuple2(false, log10)
            }

            var log10Var = log10
            while (true) {
                // invariant: x != 0 && x * 10^log10 == old(x)
                if (x == BigInteger.ONE) {
                    return Tuple2(true, log10Var)
                } else if (x.mod(BigInteger.TEN) == BigInteger.ZERO) {
                    log10Var++
                    x = x.divide(BigInteger.TEN)
                } else {
                    return Tuple2(false, log10Var)
                }
            }
        }

        fun dividesAPowerOf10(i: BigInteger): Tuple3<Boolean, BigInteger, Int> {
            var i = i
            var factor = BigInteger.ONE
            var log10 = 0
            if (i.compareTo(BigInteger.ZERO) <= 0) {
                return Tuple3(false, factor, log10)
            }

            // invariant: 1 <= i && i * 10^log10 == factor * old(i)
            while (i.mod(BigInteger.TEN) == BigInteger.ZERO) {
                i = i.divide(BigInteger.TEN)
                log10++
            }

            val two = BigInteger.valueOf(2) // note, in Java 9, one can use BigInteger.TWO
            val five = BigInteger.valueOf(5)
            while (i.mod(five) == BigInteger.ZERO) {
                i = i.divide(five)
                factor = factor.multiply(two)
                log10++
            }
            while (i.mod(two) == BigInteger.ZERO) {
                i = i.divide(two)
                factor = factor.multiply(five)
                log10++
            }

            return Tuple3(i == BigInteger.ONE, factor, log10)
        }

        // In order to compare, add, and subtract fractions, they must have the same denominator. This computes the
        // common denominator of the fractions, and returns a tuple containing:
        // aa: the numerator for a when multiplied by the common denominator
        // bb: the numerator for b when multiplied by the common denominator
        // dd: the common denominator
        private fun Normalize(a: BigRational, b: BigRational): Tuple3<BigInteger, BigInteger, BigInteger> {
            val aa: BigInteger
            val bb: BigInteger
            val dd: BigInteger
            if (a.num == BigInteger.ZERO) {
                aa = a.num
                bb = b.num
                dd = b.den
            } else if (b.num == BigInteger.ZERO) {
                aa = a.num
                dd = a.den
                bb = b.num
            } else {
                val gcd = a.den.gcd(b.den)
                val xx = a.den.divide(gcd)
                val yy = b.den.divide(gcd)
                // We now have a == a.num / (xx * gcd) and b == b.num / (yy * gcd).
                aa = a.num.multiply(yy)
                bb = b.num.multiply(xx)
                // 1 <= a.den * yy -> 1 <= dd
                dd = a.den.multiply(yy)
            }
            return Tuple3(aa, bb, dd)
        }

        fun add(a: BigRational, b: BigRational): BigRational {
            val t = Normalize(a, b)
            return BigRational(t.dtor__0().add(t.dtor__1()), t.dtor__2())
        }

        fun subtract(a: BigRational, b: BigRational): BigRational {
            val t = Normalize(a, b)
            return BigRational(t.dtor__0().subtract(t.dtor__1()), t.dtor__2())
        }

        fun negate(a: BigRational): BigRational {
            return BigRational(a.num.negate(), a.den)
        }

        fun multiply(a: BigRational, b: BigRational): BigRational {
            return BigRational(a.num.multiply(b.num), a.den.multiply(b.den))
        }

        fun divide(a: BigRational, b: BigRational): BigRational {
            val bReciprocal: BigRational
            if (0 < b.num.signum()) {
                bReciprocal = BigRational(b.den, b.num)
            } else {
                // We track the sign of the rational in the numerator, so ensure that the numerator of the reciprocal
                // has the sign of rational.
                bReciprocal = BigRational(b.den.negate(), b.num.negate())
            }

            return multiply(a, bReciprocal)
        }
    }
}
