package dafny

/**
 * An Int wrapper used as a more type-safe reference to a Unicode scalar value
 * specifically, which corresponds to the Dafny `char` type when using --unicode-char.
 *
 * Reflection- and java-free: valid-code-point / surrogate checks are done with plain
 * integer range tests (the Unicode surrogate range is D800..DFFF, the maximum code
 * point is 0x10FFFF), so this compiles for every Kotlin Multiplatform target.
 */
class CodePoint private constructor(private val value: Int) {

    init {
        if (!isValidCodePoint(value) || (MIN_SURROGATE <= value && value <= MAX_SURROGATE)) {
            throw IllegalArgumentException("Code point out of range: $value")
        }
    }

    override fun equals(obj: Any?): Boolean {
        if (obj == null || obj !is CodePoint) {
            return false
        }
        return value == obj.value
    }

    override fun hashCode(): Int = value.hashCode()

    fun value(): Int = value

    override fun toString(): String = Helpers.ToCharLiteral(value)

    // Caching a subset of values just like the built-in box types.
    private object CodePointCache {
        const val MAX_CACHE_KEY = 128

        val cache: kotlin.Array<CodePoint?> = arrayOfNulls(MAX_CACHE_KEY)

        init {
            for (i in cache.indices) {
                cache[i] = CodePoint(i)
            }
        }
    }

    companion object {
        private const val MIN_SURROGATE = 0xD800
        private const val MAX_SURROGATE = 0xDFFF
        private const val MAX_CODE_POINT = 0x10FFFF

        private fun isValidCodePoint(cp: Int): Boolean = cp in 0..MAX_CODE_POINT

        fun valueOf(value: Int): CodePoint {
            if (value in 0 until CodePointCache.MAX_CACHE_KEY) {
                return CodePointCache.cache[value]!!
            }
            return CodePoint(value)
        }

        fun isCodePoint(i: BigInteger): Boolean {
            return (i.signum() != -1 && i.compareTo(BigInteger.valueOf(0xD800)) < 0) ||
                (i.compareTo(BigInteger.valueOf(0xE000)) >= 0 && i.compareTo(BigInteger.valueOf(0x11_0000)) < 0)
        }

        fun hashCode(value: Int): Int = value.hashCode()
    }
}
