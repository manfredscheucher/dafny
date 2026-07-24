@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "NAME_SHADOWING", "FunctionName")

// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny

// Covariant (`out T`) to match Dafny's covariant sequences. Sequences are immutable, so T is
// overwhelmingly in output position; the few in-position uses (generic containers built and
// returned internally, the Copier plumbing) are @UnsafeVariance and provably safe.
abstract class DafnySequence<out T> : Iterable<T> {
    /*
    Invariant: forall 0<=i<length(). seq[i] == T || null
    Property: DafnySequences are immutable. Any methods that seem to edit the DafnySequence will only return a new
    DafnySequence
    */

    abstract fun elementType(): TypeDescriptor<@UnsafeVariance T>

    open fun toArray(): Array<@UnsafeVariance T> {
        return Array.fromList(elementType(), asList())
    }

    fun toRawArray(): Any {
        return toArray().unwrap()
    }

    // Determines if this DafnySequence is a prefix of other
    fun <U> isPrefixOf(other: DafnySequence<U>): Boolean {
        require(other != null) { "Precondition Violation" }
        if (other.length() < length()) return false
        for (i in 0 until length()) {
            if (this.select(i) != other.select(i)) return false
        }
        return true
    }

    // Determines if this DafnySequence is a proper prefix of other
    fun <U> isProperPrefixOf(other: DafnySequence<U>): Boolean {
        require(other != null) { "Precondition Violation" }
        return length() < other.length() && isPrefixOf(other)
    }

    // This is just a convenience to implement infrequently-used operations or
    // non-performance-critical operations.  Uses of it may be specialized in
    // subclasses as needed.
    internal open fun asList(): MutableList<@UnsafeVariance T> {
        return object : kotlin.collections.AbstractMutableList<@UnsafeVariance T>() {
            override fun get(index: Int): T {
                return select(index)
            }

            override val size: Int
                get() = length()

            override fun set(index: Int, element: @UnsafeVariance T): T {
                throw UnsupportedOperationException()
            }

            override fun add(index: Int, element: @UnsafeVariance T) {
                throw UnsupportedOperationException()
            }

            override fun removeAt(index: Int): T {
                throw UnsupportedOperationException()
            }

            override fun iterator(): MutableIterator<@UnsafeVariance T> {
                return this@DafnySequence.iterator() as MutableIterator<T>
            }
        }
    }

    // Build a new sequence of the same type, and a known length, by copying
    // from a number of existing ones.
    // Not public; only meant to be used by ConcatDafnySequence.
    internal abstract fun newCopier(length: Int): Copier<@UnsafeVariance T>

    interface Copier<T> {
        fun copyFrom(source: DafnySequence<T>)
        fun result(): NonLazyDafnySequence<T>
    }

    abstract fun select(i: Int): T

    fun selectUnsigned(i: Byte): T {
        return select(i.toInt() and 0xFF)
    }

    fun selectUnsigned(i: Short): T {
        return select(i.toInt() and 0xFFFF)
    }

    fun selectUnsigned(i: Int): T {
        return select((i.toLong() and 0xFFFFFFFFL))
    }

    fun select(i: Long): T {
        return select(BigInteger.valueOf(i))
    }

    fun selectUnsigned(i: Long): T {
        return select(Helpers.unsignedToBigInteger(i))
    }

    fun select(i: BigInteger): T {
        return select(i.toInt())
    }

    abstract fun length(): Int

    open fun isEmpty(): Boolean {
        return this.length() == 0
    }

    fun cardinalityInt(): Int {
        return length()
    }

    abstract fun <R> update(i: Int, t: R): DafnySequence<R>

    open fun contains(t: Any?): Boolean {
        require(t != null) { "Precondition Violation" }
        return asList().indexOf(t as T) != -1
    }

    // Returns the subsequence of values [lo..hi)
    abstract fun subsequence(lo: Int, hi: Int): DafnySequence<T>

    // Returns the subsequence of values [lo..length())
    fun drop(lo: Int): DafnySequence<T> {
        require(lo >= 0 && lo <= length()) { "Precondition Violation" }
        return subsequence(lo, length())
    }

    fun dropUnsigned(lo: Byte): DafnySequence<T> {
        return drop(lo.toInt() and 0xFF)
    }

    fun dropUnsigned(lo: Short): DafnySequence<T> {
        return drop(lo.toInt() and 0xFFFF)
    }

    fun dropUnsigned(lo: Int): DafnySequence<T> {
        return drop((lo.toLong() and 0xFFFFFFFFL))
    }

    fun drop(lo: Long): DafnySequence<T> {
        return drop(BigInteger.valueOf(lo))
    }

    fun dropUnsigned(lo: Long): DafnySequence<T> {
        return drop(Helpers.unsignedToBigInteger(lo))
    }

    fun drop(lo: BigInteger): DafnySequence<T> {
        return drop(lo.toInt())
    }

    // Returns the subsequence of values [0..hi)
    fun take(hi: Int): DafnySequence<T> {
        require(hi >= 0 && hi <= length()) { "Precondition Violation" }
        return subsequence(0, hi)
    }

    fun takeUnsigned(hi: Byte): DafnySequence<T> {
        return take(hi.toInt() and 0xFF)
    }

    fun takeUnsigned(hi: Short): DafnySequence<T> {
        return take(hi.toInt() and 0xFFFF)
    }

    fun takeUnsigned(hi: Int): DafnySequence<T> {
        return take((hi.toLong() and 0xFFFFFFFFL))
    }

    fun take(hi: Long): DafnySequence<T> {
        return take(BigInteger.valueOf(hi))
    }

    fun takeUnsigned(hi: Long): DafnySequence<T> {
        return take(Helpers.unsignedToBigInteger(hi))
    }

    fun take(hi: BigInteger): DafnySequence<T> {
        return take(hi.toInt())
    }

    fun slice(l: List<Int>): DafnySequence<out DafnySequence<out T>> {
        require(l != null) { "Precondition Violation" }
        val list = ArrayList<DafnySequence<out T>>()
        var curr = 0
        for (i in l) {
            require(i != null) { "Precondition Violation" }
            list.add(subsequence(curr, curr + i))
            curr += i
        }

        val eexx = elementType()
        val ssxx = _typeDescriptor(eexx)
        return fromList(ssxx, list)
    }

    fun asDafnyMultiset(): DafnyMultiset<T> {
        return DafnyMultiset(asList())
    }

    abstract override fun iterator(): MutableIterator<T>

    final override fun equals(obj: Any?): Boolean {
        if (this === obj) {
            return true
        }
        if (obj !is DafnySequence<*>) {
            return false
        }
        val other = obj as DafnySequence<T>
        return this.equalsNonLazy(other.force())
    }

    // Compare for equality to the given sequence, assuming that it is not
    // null, not == to this, and not lazy.
    internal open fun equalsNonLazy(other: NonLazyDafnySequence<@UnsafeVariance T>): Boolean {
        return asList() == other.asList()
    }

    abstract override fun hashCode(): Int

    override fun toString(): String {
        return asList().toString()
    }

    open fun verbatimString(): String {
        if (elementType() === TypeDescriptor.UNICODE_CHAR) {
            // This is slow, but the override in ArrayDafnySequence will almost
            // always be used instead
            val codePoints = IntArray(length())
            var i = 0
            for (ch in asList() as List<Int>) {
                codePoints[i++] = ch
            }
            return Helpers.codePointsToString(codePoints, 0, codePoints.size)
        } else {
            // This is slow, but the override in StringDafnySequence will almost
            // always be used instead
            val builder = StringBuilder()
            for (ch in asList() as List<Char>) {
                builder.append(ch)
            }
            return builder.toString()
        }
    }

    fun Elements(): Iterable<T> {
        return this
    }

    fun UniqueElements(): HashSet<@UnsafeVariance T> {
        return HashSet(asList())
    }

    // @return The sequence representing this sequence's actual value.
    // That's usually just the sequence itself, but not if the
    // sequence is lazily computed.
    internal abstract fun force(): NonLazyDafnySequence<@UnsafeVariance T>

    companion object {
        fun <T> of(type: TypeDescriptor<T>, vararg elements: T): DafnySequence<T> {
            // Build the array element-wise via the TypeDescriptor, which dispatches
            // to the correct (boxed or unboxed) representation for the element type.
            val array: Array<T> = Array.newArray(type, elements.size)
            for (i in elements.indices) {
                array.set(i, elements[i])
            }
            return fromArray(type, array)
        }

        fun of(vararg elements: Byte): DafnySequence<Byte> {
            return fromArray(TypeDescriptor.BYTE, Array.wrap(elements))
        }

        fun of(vararg elements: Short): DafnySequence<Short> {
            return fromArray(TypeDescriptor.SHORT, Array.wrap(elements))
        }

        fun of(vararg elements: Int): DafnySequence<Int> {
            return fromArray(TypeDescriptor.INT, Array.wrap(elements))
        }

        fun of(vararg elements: Long): DafnySequence<Long> {
            return fromArray(TypeDescriptor.LONG, Array.wrap(elements))
        }

        fun of(vararg elements: Boolean): DafnySequence<Boolean> {
            return fromArray(TypeDescriptor.BOOLEAN, Array.wrap(elements))
        }

        fun of(vararg elements: Char): DafnySequence<Char> {
            return fromArray(TypeDescriptor.CHAR, Array.wrap(elements))
        }

        fun <T> empty(type: TypeDescriptor<T>): DafnySequence<T> {
            if (type === TypeDescriptor.CHAR) {
                return asString("") as DafnySequence<T>
            }
            return ArrayDafnySequence.empty(type)
        }

        fun <T> fromArray(type: TypeDescriptor<T>, elements: Array<T>): DafnySequence<T> {
            return fromRawArray(type, elements.unwrap())
        }

        fun <T> fromRawArray(type: TypeDescriptor<T>, elements: Any): DafnySequence<T> {
            if (type === TypeDescriptor.CHAR) {
                return asString((elements as CharArray).concatToString()) as DafnySequence<T>
            }
            return ArrayDafnySequence(Array.wrap(type, elements).copy())
        }

        // Return a sequence backed by the given array without making a defensive
        // copy.  Only safe if the array never changes afterward.
        fun <T> unsafeWrapArray(elements: Array<T>): DafnySequence<T> {
            return ArrayDafnySequence(elements, true)
        }

        fun <T> unsafeWrapRawArray(type: TypeDescriptor<T>, elements: Any): DafnySequence<T> {
            return ArrayDafnySequence(Array.wrap(type, elements))
        }

        fun <T> fromArrayRange(type: TypeDescriptor<T>, elements: Array<T>, lo: Int, hi: Int): DafnySequence<T> {
            return ArrayDafnySequence(elements.copyOfRange(lo, hi))
        }

        fun <T> fromRawArrayRange(type: TypeDescriptor<T>, elements: Any, lo: Int, hi: Int): DafnySequence<T> {
            return fromArrayRange(type, Array.wrap(type, elements), lo, hi)
        }

        fun <T> fromList(type: TypeDescriptor<T>, l: List<T>): DafnySequence<T> {
            require(l != null) { "Precondition Violation" }
            return ArrayDafnySequence(Array.fromList(type, l))
        }

        fun asString(s: String): DafnySequence<Char> {
            return StringDafnySequence(s)
        }

        // Combine a UTF-16 surrogate pair into a single Unicode code point.
        // Kotlin-Multiplatform equivalent of the JVM's Character.toCodePoint(high, low):
        // the pair encodes (cp - 0x10000) with the high 10 bits in the high surrogate
        // (offset 0xD800) and the low 10 bits in the low surrogate (offset 0xDC00).
        private fun toCodePoint(high: Char, low: Char): Int =
            0x10000 + ((high.code - 0xD800) shl 10) + (low.code - 0xDC00)

        fun asUnicodeString(s: String): DafnySequence<CodePoint> {
            // Decode the UTF-16 string to Unicode code points (java-free). A surrogate pair
            // collapses to one code point, so the count isn't known up front; collect into a
            // list, then copy into the IntArray backing a unicode-char sequence.
            val decoded = ArrayList<Int>(s.length)
            var charIndex = 0
            while (charIndex < s.length) {
                val c1 = s[charIndex++]
                if (c1.isHighSurrogate()) {
                    if (charIndex >= s.length) {
                        throw IllegalArgumentException()
                    }
                    val c2 = s[charIndex++]
                    if (!c2.isLowSurrogate()) {
                        throw IllegalArgumentException()
                    }
                    decoded.add(toCodePoint(c1, c2))
                } else {
                    decoded.add(c1.code)
                }
            }
            val codePoints = IntArray(decoded.size) { decoded[it] }
            return ArrayDafnySequence(Array.wrap(TypeDescriptor.UNICODE_CHAR, codePoints))
        }

        fun fromBytes(bytes: ByteArray): DafnySequence<Byte> {
            return unsafeWrapBytes(bytes.copyOf())
        }

        // Return a sequence backed by the given byte array without making a
        // defensive copy.  Only safe if the array never changes afterward.
        fun unsafeWrapBytes(bytes: ByteArray): DafnySequence<Byte> {
            return unsafeWrapArray(Array.wrap(bytes))
        }

        fun <T> Create(type: TypeDescriptor<T>, length: BigInteger, init: (BigInteger) -> T): DafnySequence<T> {
            val len = length.intValueExact()
            val values = Array.newArray(type, len)
            for (i in 0 until len) {
                values.set(i, init(BigInteger.valueOf(i.toLong())))
            }
            return fromArray(type, values)
        }

        fun <T> _typeDescriptor(elementType: TypeDescriptor<T>): TypeDescriptor<DafnySequence<out T>> {
            return TypeDescriptor.referenceWithDefault(
                empty(elementType)
            )
        }

        fun toByteArray(seq: DafnySequence<Byte>): ByteArray {
            return Array.unwrapBytes(seq.toArray())
        }

        fun toIntArray(seq: DafnySequence<Int>): IntArray {
            return Array.unwrapInts(seq.toArray())
        }

        fun <T> concatenate(th: DafnySequence<out T>, other: DafnySequence<out T>): DafnySequence<T> {
            require(th != null) { "Precondition Violation" }
            require(other != null) { "Precondition Violation" }

            return if (th.isEmpty()) {
                other as DafnySequence<T>
            } else if (other.isEmpty()) {
                th as DafnySequence<T>
            } else {
                ConcatDafnySequence(th as DafnySequence<T>, other as DafnySequence<T>)
            }
        }

        fun <R> update(seq: DafnySequence<out R>, b: BigInteger, t: R): DafnySequence<R> {
            return seq.update(b.toInt(), t)
        }

        fun <R> update(seq: DafnySequence<out R>, idx: Int, t: R): DafnySequence<R> {
            return seq.update(idx, t)
        }

        fun <R> update(seq: DafnySequence<out R>, idx: Long, t: R): DafnySequence<R> {
            return seq.update(idx.toInt(), t)
        }
    }
}

abstract class NonLazyDafnySequence<T> : DafnySequence<T>() {
    final override fun force(): NonLazyDafnySequence<T> {
        return this
    }
}

class ArrayDafnySequence<T> : NonLazyDafnySequence<T> {
    private val seq: Array<T>

    @Suppress("unused")
    private var unsafe: Boolean // for debugging purposes

    // NOTE: Input array is *shared*; must be a copy if it comes from a public input
    constructor(elementType: TypeDescriptor<T>, elements: Any, unsafe: Boolean) :
        this(Array.wrap(elementType, elements), unsafe)

    constructor(elementType: TypeDescriptor<T>, elements: Any) :
        this(Array.wrap(elementType, elements))

    constructor(elements: Array<T>, unsafe: Boolean) {
        this.seq = elements
        this.unsafe = unsafe
    }

    constructor(array: Array<T>) : this(array, false)

    fun unwrap(): Array<T> {
        return seq
    }

    override fun toArray(): Array<T> {
        return seq.copy()
    }

    override fun elementType(): TypeDescriptor<T> {
        return seq.elementType()
    }

    override fun <R> update(i: Int, t: R): ArrayDafnySequence<R> {
        require(t != null) { "Precondition Violation" }
        require(0 <= i && i < length()) { "Precondition Violation" }
        val newArray = seq.copy() as Array<R>
        newArray.set(i, t)
        return ArrayDafnySequence(newArray)
    }

    override fun subsequence(lo: Int, hi: Int): ArrayDafnySequence<T> {
        require(lo >= 0 && hi >= 0 && hi >= lo) { "Precondition Violation" }
        return ArrayDafnySequence(seq.copyOfRange(lo, hi))
    }

    override fun newCopier(length: Int): Copier<T> {
        return object : Copier<T> {
            private val newArray: Array<T> = Array.newArray(seq.elementType(), length)
            private var nextIndex = 0

            override fun copyFrom(source: DafnySequence<T>) {
                var source = source.force()
                if (source is ArrayDafnySequence<*>) {
                    val sourceArray = (source as ArrayDafnySequence<T>).seq
                    sourceArray.copy(0, newArray, nextIndex, sourceArray.length())
                    nextIndex += sourceArray.length()
                } else {
                    for (t in source) {
                        newArray.set(nextIndex++, t)
                    }
                }
            }

            override fun result(): NonLazyDafnySequence<T> {
                return ArrayDafnySequence(newArray)
            }
        }
    }

    override fun asList(): MutableList<T> {
        return object : kotlin.collections.AbstractMutableList<T>() {
            override fun get(index: Int): T {
                return seq.get(index)
            }

            override fun set(index: Int, element: T): T {
                val prev = seq.get(index)
                seq.set(index, element)
                return prev
            }

            override fun add(index: Int, element: T) {
                throw UnsupportedOperationException()
            }

            override fun removeAt(index: Int): T {
                throw UnsupportedOperationException()
            }

            override val size: Int
                get() = length()
        }
    }

    override fun select(i: Int): T {
        return seq.get(i)
    }

    override fun length(): Int {
        return seq.length()
    }

    override fun iterator(): MutableIterator<T> {
        return asList().iterator()
    }

    override fun equalsNonLazy(other: NonLazyDafnySequence<T>): Boolean {
        return if (other is ArrayDafnySequence<*>) {
            seq.shallowEquals((other as ArrayDafnySequence<T>).seq)
        } else {
            super.equalsNonLazy(other)
        }
    }

    override fun hashCode(): Int {
        return asList().hashCode()
    }

    override fun verbatimString(): String {
        return if (elementType() === TypeDescriptor.UNICODE_CHAR) {
            Helpers.codePointsToString(seq.unwrap() as IntArray, 0, seq.length())
        } else {
            (seq.unwrap() as CharArray).concatToString()
        }
    }

    companion object {
        fun <T> empty(type: TypeDescriptor<T>): ArrayDafnySequence<T> {
            return ArrayDafnySequence(type, type.newArray(0))
        }
    }
}

class StringDafnySequence : NonLazyDafnySequence<Char> {
    private val string: String

    constructor(string: String) {
        this.string = string
    }

    override fun toArray(): Array<Char> {
        return Array.wrap(string.toCharArray())
    }

    override fun elementType(): TypeDescriptor<Char> {
        return TypeDescriptor.CHAR
    }

    override fun select(i: Int): Char {
        return string[i]
    }

    override fun length(): Int {
        return string.length
    }

    override fun <R> update(i: Int, t: R): DafnySequence<R> {
        // assume R == Character
        require(t != null) { "Precondition Violation" }
        val sb = StringBuilder(string)
        sb[i] = t as Char
        return StringDafnySequence(sb.toString()) as DafnySequence<R>
    }

    override fun contains(t: Any?): Boolean {
        require(t != null) { "Precondition Violation" }
        return string.indexOf(t as Char) != -1
    }

    override fun subsequence(lo: Int, hi: Int): DafnySequence<Char> {
        return StringDafnySequence(string.substring(lo, hi))
    }

    override fun newCopier(length: Int): Copier<Char> {
        return object : Copier<Char> {
            private val sb = StringBuilder()

            override fun copyFrom(source: DafnySequence<Char>) {
                var source = source.force()
                if (source is StringDafnySequence) {
                    sb.append((source as StringDafnySequence).string)
                } else {
                    for (c in source) {
                        sb.append(c)
                    }
                }
            }

            override fun result(): NonLazyDafnySequence<Char> {
                return StringDafnySequence(sb.toString())
            }
        }
    }

    override fun iterator(): MutableIterator<Char> {
        val n = string.length
        return object : MutableIterator<Char> {
            var i = 0

            override fun hasNext(): Boolean {
                return i < n
            }

            override fun next(): Char {
                return string[i++]
            }

            override fun remove() {
                throw UnsupportedOperationException()
            }
        }
    }

    override fun equalsNonLazy(other: NonLazyDafnySequence<Char>): Boolean {
        return if (other is StringDafnySequence) {
            string == (other as StringDafnySequence).string
        } else {
            super.equalsNonLazy(other)
        }
    }

    override fun hashCode(): Int {
        return string.hashCode()
    }

    override fun verbatimString(): String {
        return string
    }

    override fun toString(): String {
        return string
    }
}

abstract class LazyDafnySequence<T> : DafnySequence<T>() {
    override fun toArray(): Array<T> {
        return force().toArray()
    }

    override fun elementType(): TypeDescriptor<T> {
        return force().elementType()
    }

    override fun asList(): MutableList<T> {
        return force().asList()
    }

    override fun select(i: Int): T {
        return force().select(i)
    }

    override fun length(): Int {
        return force().length()
    }

    override fun <R> update(i: Int, t: R): DafnySequence<R> {
        return force().update(i, t)
    }

    override fun subsequence(lo: Int, hi: Int): DafnySequence<T> {
        return force().subsequence(lo, hi)
    }

    override fun newCopier(length: Int): Copier<T> {
        return force().newCopier(length)
    }

    override fun iterator(): MutableIterator<T> {
        return force().iterator()
    }

    override fun toString(): String {
        return force().toString()
    }

    override fun verbatimString(): String {
        return force().verbatimString()
    }

    override fun equalsNonLazy(other: NonLazyDafnySequence<T>): Boolean {
        return force().equalsNonLazy(other)
    }

    override fun hashCode(): Int {
        return force().hashCode()
    }
}

class ConcatDafnySequence<T> : LazyDafnySequence<T> {
    // INVARIANT: Either these are both non-null and ans is null or both are
    // null and ans is non-null.
    private var left: DafnySequence<T>?

    private var right: DafnySequence<T>?
    private var ans: NonLazyDafnySequence<T>? = null
    private val length: Int

    constructor(left: DafnySequence<T>, right: DafnySequence<T>) {
        this.left = left
        this.right = right
        this.length = left.length() + right.length()
    }

    override fun force(): NonLazyDafnySequence<T> {
        if (ans == null) {
            ans = computeElements()
            // Allow left and right to be garbage-collected
            left = null
            right = null
        }
        return ans!!
    }

    override fun length(): Int {
        return length
    }

    private fun computeElements(): NonLazyDafnySequence<T> {
        // Somewhat arbitrarily, the copier will be created by the leftmost
        // sequence.
        val copier: Copier<T>

        // Treat this instance as the root of a tree, and prepare to perform a
        // non-recursive in-order traversal.
        val toVisit = kotlin.collections.ArrayDeque<DafnySequence<T>>()

        // Another thread may have already completed force() at this point.
        var leftBuffer = left
        var rightBuffer = right
        if (leftBuffer == null || rightBuffer == null) {
            return ans!!
        }

        toVisit.addLast(rightBuffer)
        var first: DafnySequence<T> = leftBuffer
        while (first is ConcatDafnySequence<*>) {
            val cfirst = first as ConcatDafnySequence<T>
            leftBuffer = cfirst.left
            rightBuffer = cfirst.right
            if (leftBuffer == null || rightBuffer == null) {
                break
            } else {
                toVisit.addLast(rightBuffer)
                first = leftBuffer
            }
        }
        toVisit.addLast(first)

        copier = first.newCopier(this.length)

        while (!toVisit.isEmpty()) {
            val seq = toVisit.removeLast()

            if (seq is ConcatDafnySequence<*>) {
                val cseq = seq as ConcatDafnySequence<T>

                leftBuffer = cseq.left
                rightBuffer = cseq.right
                if (leftBuffer == null || rightBuffer == null) {
                    copier.copyFrom(cseq.ans!!)
                } else {
                    toVisit.addLast(rightBuffer)
                    toVisit.addLast(leftBuffer)
                }
            } else {
                copier.copyFrom(seq)
            }
        }

        return copier.result()
    }
}
