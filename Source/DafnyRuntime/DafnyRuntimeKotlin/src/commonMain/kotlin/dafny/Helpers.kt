// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

@file:Suppress("FunctionName", "UNCHECKED_CAST", "unused")

package dafny

// Terminate the process with the given exit code. `exitProcess` isn't available in every
// Kotlin Multiplatform common source set (e.g. JS), so it's an expect/actual per platform.
expect fun dafnyExit(code: Int): Nothing

// Reflection- and java-free runtime helpers. Predicates/functions are Kotlin function
// types, iteration uses Kotlin Sequences, and unsigned arithmetic uses Kotlin's UInt/
// ULong — all Kotlin Multiplatform friendly.
object Helpers {

    fun FromMainArguments(args: kotlin.Array<String>): DafnySequence<out DafnySequence<out Char>> {
        val type: TypeDescriptor<DafnySequence<out Char>> =
            DafnySequence._typeDescriptor<Char>(TypeDescriptor.CHAR)
        val dafnyArgs: Array<DafnySequence<out Char>> = Array.newArray(type, args.size + 1)
        dafnyArgs.set(0, DafnySequence.asString("dafny"))
        for (i in args.indices) dafnyArgs.set(i + 1, DafnySequence.asString(args[i]))
        return DafnySequence.fromArray(type, dafnyArgs)
    }

    fun UnicodeFromMainArguments(args: kotlin.Array<String>): DafnySequence<out DafnySequence<out CodePoint>> {
        val type: TypeDescriptor<DafnySequence<out CodePoint>> =
            DafnySequence._typeDescriptor<CodePoint>(TypeDescriptor.UNICODE_CHAR)
        val dafnyArgs: Array<DafnySequence<out CodePoint>> = Array.newArray(type, args.size + 1)
        dafnyArgs.set(0, DafnySequence.asUnicodeString("dafny"))
        for (i in args.indices) dafnyArgs.set(i + 1, DafnySequence.asUnicodeString(args[i]))
        return DafnySequence.fromArray(type, dafnyArgs)
    }

    fun ToStringLiteral(dafnyString: DafnySequence<out CodePoint>): String {
        val builder = StringBuilder()
        builder.append("\"")
        for (codePoint in dafnyString) {
            AppendCodePointWithEscaping(builder, codePoint.value())
        }
        builder.append("\"")
        return builder.toString()
    }

    // Note this is a Dafny character literal, not necessarily a valid host character literal
    fun ToCharLiteral(codePoint: Int): String {
        val builder = StringBuilder()
        builder.append("'")
        AppendCodePointWithEscaping(builder, codePoint)
        builder.append("'")
        return builder.toString()
    }

    private fun AppendCodePointWithEscaping(builder: StringBuilder, codePoint: Int) {
        when (codePoint) {
            '\n'.code -> builder.append("\\n")
            '\r'.code -> builder.append("\\r")
            '\t'.code -> builder.append("\\t")
            0 -> builder.append("\\0")
            '\''.code -> builder.append("\\'")
            '"'.code -> builder.append("\\\"")
            '\\'.code -> builder.append("\\\\")
            else -> appendCodePoint(builder, codePoint)
        }
    }

    // Append a Unicode code point to a StringBuilder, encoded as UTF-16.
    // Kotlin-Multiplatform equivalent of the JVM's StringBuilder.appendCodePoint(int):
    // code points in the Basic Multilingual Plane (<= 0xFFFF) are a single char; higher
    // code points are split into a high/low surrogate pair (the inverse of toCodePoint).
    private fun appendCodePoint(builder: StringBuilder, codePoint: Int) {
        if (codePoint <= 0xFFFF) {
            builder.append(codePoint.toChar())
        } else {
            val cp = codePoint - 0x10000
            val high = 0xD800 + (cp shr 10)   // high 10 bits -> high surrogate
            val low = 0xDC00 + (cp and 0x3FF) // low 10 bits  -> low surrogate
            builder.append(high.toChar())
            builder.append(low.toChar())
        }
    }

    // Build a String from an array of Unicode code points (KMP-native replacement for the
    // JVM String(int[] codePoints, offset, count) constructor).
    fun codePointsToString(codePoints: IntArray, offset: Int, count: Int): String {
        val builder = StringBuilder()
        for (i in offset until offset + count) {
            appendCodePoint(builder, codePoints[i])
        }
        return builder.toString()
    }

    fun <T> Quantifier(vals: Iterable<T>, frall: Boolean, pred: (T) -> Boolean): Boolean {
        for (t in vals) {
            if (pred(t) != frall) {
                return !frall
            }
        }
        return frall
    }

    // The generator emits calls to `dafny.Helpers.quantifier` (lowercase).
    fun <T> quantifier(vals: Iterable<T>, frall: Boolean, pred: (T) -> Boolean): Boolean =
        Quantifier(vals, frall, pred)

    // Null-safe value equality. Kotlin-Multiplatform equivalent of java.util.Objects.equals:
    // two nulls are equal, otherwise defer to `a.equals(b)`. Used by the generator for
    // equality of value types that aren't directly comparable with `==`.
    fun areEqual(a: Any?, b: Any?): Boolean = a == b

    // Null-safe hash code. Equivalent of java.util.Objects.hashCode (null -> 0).
    fun hashCode(o: Any?): Int = o?.hashCode() ?: 0

    fun <T> Id(t: T): T = t

    fun <T, U> Let(t: T, f: (T) -> U): U = f(t)

    /* Returns Iterable in range [lo, hi-1] if lo and hi are both not null.
    If lo == null, returns Iterable that infinitely ranges down from hi-1.
    If hi == null, returns Iterable that infinitely ranges up from lo.
     */
    fun IntegerRange(lo: BigInteger?, hi: BigInteger?): Iterable<BigInteger> {
        require(lo != null || hi != null)
        return when {
            lo == null -> Iterable {
                generateSequence(hi!!.subtract(BigInteger.ONE)) { i -> i.subtract(BigInteger.ONE) }.iterator()
            }
            hi == null -> Iterable {
                generateSequence(lo) { i -> i.add(BigInteger.ONE) }.iterator()
            }
            else -> {
                val loNN: BigInteger = lo
                val hiNN: BigInteger = hi
                Iterable {
                    object : Iterator<BigInteger> {
                        private var i = loNN
                        override fun hasNext(): Boolean = i.compareTo(hiNN) < 0
                        override fun next(): BigInteger {
                            val j = i
                            i = i.add(BigInteger.ONE)
                            return j
                        }
                    }
                }
            }
        }
    }

    fun AllIntegers(): Iterable<BigInteger> = Iterable {
        object : Iterator<BigInteger> {
            private var i = BigInteger.ZERO
            override fun hasNext(): Boolean = true
            override fun next(): BigInteger {
                val j = i
                i = when {
                    i == BigInteger.ZERO -> BigInteger.ONE
                    i.signum() > 0 -> i.negate()
                    else -> i.negate().add(BigInteger.ONE)
                }
                return j
            }
        }
    }

    fun AllBooleans(): Iterable<Boolean> = listOf(false, true)

    fun AllChars(): Iterable<Char> = Iterable {
        (0 until 0x1_0000).asSequence().map { it.toChar() }.iterator()
    }

    fun AllUnicodeChars(): Iterable<CodePoint> = Iterable {
        ((0 until 0xD800).asSequence() + (0xE000 until 0x11_0000).asSequence())
            .map { CodePoint.valueOf(it) }.iterator()
    }

    fun <G> toString(g: G): String {
        return g?.toString() ?: "null"
    }

    fun toInt(i: BigInteger): Int = i.toInt()

    fun outOfRange(msg: String?) {
        throw DafnyHaltException(msg)
    }

    fun toIntChecked(i: BigInteger, msg: String?): Int {
        val r = i.toInt()
        if (BigInteger.valueOf(r.toLong()) != i) {
            val m = (msg ?: "value out of range for a 32-bit int") + ": " + i
            outOfRange(m)
        }
        return r
    }

    fun toIntChecked(i: Long, msg: String?): Int {
        val r = i.toInt()
        if (r.toLong() != i) {
            val m = (msg ?: "value out of range for a 32-bit int") + ": " + i
            outOfRange(m)
        }
        return r
    }

    // Small signed native types always fit in an Int (used e.g. for array dimensions
    // declared with a `byte`/`short`/`int` native type).
    fun toIntChecked(i: Byte, msg: String?): Int = i.toInt()
    fun toIntChecked(i: Short, msg: String?): Int = i.toInt()
    fun toIntChecked(i: Int, msg: String?): Int = i

    fun unsignedToIntChecked(i: Byte): Int = unsignedToInt(i)

    fun unsignedToIntChecked(i: Short): Int = unsignedToInt(i)

    fun unsignedToIntChecked(i: Long, msg: String?): Int {
        val r = unsignedToInt(i)
        if (r.toLong() != i) {
            val m = (msg ?: "value out of range for a 32-bit int") + ": " + i
            outOfRange(m)
        }
        return r
    }

    fun toInt(i: Int): Int = i

    fun toInt(l: Long): Int = l.toInt()

    fun toInt(b: Byte): Int = b.toInt()
    fun toInt(s: Short): Int = s.toInt()

    fun unsignedToInt(x: Byte): Int = x.toInt() and 0xFF

    fun unsignedToInt(x: Short): Int = x.toInt() and 0xFFFF

    fun unsignedToInt(x: Long): Int = x.toInt()

    private val BYTE_LIMIT = BigInteger.of("256")                   // 0x1_00
    private val USHORT_LIMIT = BigInteger.of("65536")               // 0x1_0000
    private val UINT_LIMIT = BigInteger.of("4294967296")            // 0x1_0000_0000
    private val ULONG_LIMIT = BigInteger.of("18446744073709551616") // 0x1_0000_0000_0000_0000

    private fun unsignedToBigInteger_h(i: BigInteger, LIMIT: BigInteger): BigInteger {
        return if (i.signum() == -1) i.add(LIMIT) else i
    }

    fun unsignedToBigInteger(b: Byte): BigInteger =
        unsignedToBigInteger_h(BigInteger.valueOf(b.toLong()), BYTE_LIMIT)

    fun unsignedToBigInteger(s: Short): BigInteger =
        unsignedToBigInteger_h(BigInteger.valueOf(s.toLong()), USHORT_LIMIT)

    fun unsignedToBigInteger(i: Int): BigInteger =
        unsignedToBigInteger_h(BigInteger.valueOf(i.toLong()), UINT_LIMIT)

    fun unsignedToBigInteger(l: Long): BigInteger =
        unsignedToBigInteger_h(BigInteger.valueOf(l), ULONG_LIMIT)

    // Alias maintained only for backwards compatability
    fun unsignedLongToBigInteger(l: Long): BigInteger = unsignedToBigInteger(l)

    // Kotlin's UInt/ULong give KMP-native unsigned division/remainder (no java statics).
    fun divideUnsignedByte(a: Byte, b: Byte): Byte =
        ((a.toInt() and 0xFF).toUInt() / (b.toInt() and 0xFF).toUInt()).toInt().toByte()

    fun divideUnsignedShort(a: Short, b: Short): Short =
        ((a.toInt() and 0xFFFF).toUInt() / (b.toInt() and 0xFFFF).toUInt()).toInt().toShort()

    fun remainderUnsignedByte(a: Byte, b: Byte): Byte =
        ((a.toInt() and 0xFF).toUInt() % (b.toInt() and 0xFF).toUInt()).toInt().toByte()

    fun remainderUnsignedShort(a: Short, b: Short): Short =
        ((a.toInt() and 0xFFFF).toUInt() % (b.toInt() and 0xFFFF).toUInt()).toInt().toShort()

    // Explanation (G = original, g = opposite)
    // a = 1XXX,YYYY
    // (int)a = 1111,1111,...,1111,1XXX,YYYY (power of two's complement)
    // (int)a & 0xFF = 0000,0000,...,0000,1XXX,YYYY
    // Now right-shift works nicely
    fun bv8ShiftRight(a: Byte, amount: Byte): Int {
        return if (a < 0) {
            (a.toInt() and 0xFF) ushr amount.toInt()
        } else {
            a.toInt() ushr amount.toInt()
        }
    }

    fun bv16ShiftRight(a: Short, amount: Byte): Int {
        return if (a < 0) {
            (a.toInt() and 0xFFFF) ushr amount.toInt()
        } else {
            a.toInt() ushr amount.toInt()
        }
    }

    fun bv32ShiftRight(a: Int, amount: Byte): Int {
        if (amount.toInt() == 32) { // Only the 5 lower bits are considered and Dafny goes up to 32
            return 0
        }
        return a ushr amount.toInt()
    }

    fun bv64ShiftRight(a: Long, amount: Byte): Long {
        if (amount.toInt() == 64) { // Only the 6 lower bits are considered and Dafny goes up to 64
            return 0
        }
        return a ushr amount.toInt()
    }

    // Byte/Short have no shl in Kotlin, so shift on Int and mask back to the
    // bitvector width (Dafny truncates the result). Returns Int; the generator
    // wraps with .toByte()/.toShort().
    fun bv8ShiftLeft(a: Byte, amount: Byte): Int {
        if (amount.toInt() >= 8) { return 0 }
        return ((a.toInt() and 0xFF) shl amount.toInt()) and 0xFF
    }

    fun bv16ShiftLeft(a: Short, amount: Byte): Int {
        if (amount.toInt() >= 16) { return 0 }
        return ((a.toInt() and 0xFFFF) shl amount.toInt()) and 0xFFFF
    }

    fun bv32ShiftLeft(a: Int, amount: Byte): Int {
        if (amount.toInt() == 32) { // Only the 5 lower bits are considered and Dafny goes up to 32
            return 0
        }
        return a shl amount.toInt()
    }

    fun bv64ShiftLeft(a: Long, amount: Byte): Long {
        if (amount.toInt() == 64) { // Only the 6 lower bits are considered and Dafny goes up to 64
            return 0
        }
        return a shl amount.toInt()
    }

    fun withHaltHandling(runnable: () -> Unit) {
        try {
            runnable()
        } catch (e: DafnyHaltException) {
            println("[Program halted] " + e.message)
            dafnyExit(1)
        }
    }
}
