@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "NAME_SHADOWING", "FunctionName")

// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny


// Covariant (`out T`) to match Dafny's covariant multisets; T-in-input positions are
// @UnsafeVariance, and innerMap is `internal` so companion operations can read another
// instance's backing map.
class DafnyMultiset<out T> {
    /*
    Invariant: forall x. m.get(x) == null || m.get(x) > 0
    As in Java, null is allowed as a key
    */
    internal val innerMap: MutableMap<@UnsafeVariance T, BigInteger>

    constructor() {
        innerMap = HashMap()
    }

    // Requires that all values in m are non-negative
    constructor(m: Map<@UnsafeVariance T, BigInteger>) {
        require(m != null) { "Precondition Violation" }
        innerMap = HashMap()
        for (e in m.entries) {
            val n = e.value
            val cmp = n.compareTo(BigInteger.ZERO)
            require(0 <= cmp) { "Precondition Violation" }
            if (0 < cmp) {
                innerMap[e.key] = n
            }
        }
    }

    constructor(s: Set<@UnsafeVariance T>) {
        require(s != null) { "Precondition Violation" }
        innerMap = HashMap()
        for (t in s) {
            incrementMultiplicity(t, BigInteger.ONE)
        }
    }

    constructor(c: Collection<@UnsafeVariance T>) {
        require(c != null) { "Precondition Violation" }
        innerMap = HashMap()
        for (t in c) {
            incrementMultiplicity(t, BigInteger.ONE)
        }
    }

    // Adds all elements found in the list to a new DafnyMultiSet. The number of occurrences in the list becomes the
    // multiplicity in the DafnyMultiset
    constructor(l: List<@UnsafeVariance T>) {
        require(l != null) { "Precondition Violation" }
        innerMap = HashMap()
        for (t in l) {
            incrementMultiplicity(t, BigInteger.ONE)
        }
    }

    fun cardinality(): BigInteger {
        var sum = BigInteger.ZERO
        for (m in innerMap.values) {
            sum = sum.add(m)
        }
        return sum
    }

    // cardinalityInt should be called only if the cardinality is known to fit in an "int"
    fun cardinalityInt(): Int {
        var sum = 0
        for (m in innerMap.values) {
            sum += m.toInt()
        }
        return sum
    }

    // Determines if the current object is a subset of the DafnyMultiSet passed in. Requires that the input
    // DafnyMultiset is not null.
    fun isSubsetOf(other: DafnyMultiset<*>): Boolean {
        require(other != null) { "Precondition Violation" }
        for (entry in innerMap.entries) {
            if (multiplicity(other as DafnyMultiset<T>, entry.key).compareTo(entry.value) < 0) return false
        }
        return true
    }

    // Determines if the current object is a proper subset of the DafnyMultiSet passed in. Requires that the input
    // Dafny MultiSet is not null.
    fun isProperSubsetOf(other: DafnyMultiset<*>): Boolean {
        require(other != null) { "Precondition Violation" }
        return isSubsetOf(other) && this.cardinality().compareTo(other.cardinality()) < 0
    }

    fun contains(t: Any?): Boolean {
        // Relies on invariant that all keys have a positive multiplicity
        return innerMap.containsKey(t)
    }

    fun <U> disjoint(other: DafnyMultiset<out U>): Boolean {
        require(other != null) { "Precondition Violation" }
        for (u in other.innerMap.keys) {
            if ((innerMap as Map<Any?, BigInteger>).containsKey(u)) return false
        }
        return true
    }

    // destructively sets multiplicity of t to b; a negative value is treated as 0
    internal fun setMultiplicity(t: @UnsafeVariance T, b: BigInteger) {
        require(b != null) { "Precondition Violation" }
        if (b.compareTo(BigInteger.ZERO) > 0) {
            innerMap[t] = b
        } else {
            innerMap.remove(t)
        }
    }

    // destructively adds n (possibly negative) to value of t
    internal fun incrementMultiplicity(t: @UnsafeVariance T, b: BigInteger) {
        require(b != null) { "Precondition Violation" }
        setMultiplicity(t, multiplicity(this, t).add(b))
    }

    fun Elements(): Iterable<T> {
        val r = ArrayList<T>()
        for (e in innerMap.entries) {
            for (i in 0 until e.value.toInt()) {
                r.add(e.key)
            }
        }
        return r
    }

    fun UniqueElements(): Iterable<T> {
        return innerMap.keys
    }

    override fun equals(obj: Any?): Boolean {
        if (this === obj) return true
        if (obj == null) return false
        if (this::class != obj::class) return false
        val o = obj as DafnyMultiset<*>
        return innerMap == o.innerMap
    }

    override fun hashCode(): Int {
        return innerMap.hashCode()
    }

    override fun toString(): String {
        var s = "multiset{"
        var sep = ""
        for (entry in innerMap.entries) {
            val t = Helpers.toString(entry.key)
            val n = entry.value
            var i = BigInteger.ZERO
            while (i.compareTo(n) < 0) {
                s += sep + t
                sep = ", "
                i = i.add(BigInteger.ONE)
            }
        }
        return "$s}"
    }

    companion object {
        fun <T> of(vararg args: T): DafnyMultiset<T> {
            return DafnyMultiset(listOf(*args))
        }

        private val EMPTY: DafnyMultiset<Any?> = of<Any?>()

        fun <T> empty(): DafnyMultiset<T> {
            // Safe because immutable
            return EMPTY as DafnyMultiset<T>
        }

        fun <T> _typeDescriptor(elementType: TypeDescriptor<T>): TypeDescriptor<DafnyMultiset<out T>> {
            // Fudge the type parameter; it's not great, but it's safe because
            // (for now) type descriptors are only used for default values
            return TypeDescriptor.referenceWithDefault(empty<T>())
        }

        fun <T> multiplicity(th: DafnyMultiset<out T>, t: T): BigInteger {
            val m = th.innerMap[t]
            return m ?: BigInteger.ZERO
        }

        fun <T> update(th: DafnyMultiset<out T>, t: T, b: BigInteger): DafnyMultiset<T> {
            require(th != null) { "Precondition Violation" }
            require(b != null && b.compareTo(BigInteger.ZERO) >= 0) { "Precondition Violation" }
            val copy = DafnyMultiset((th as DafnyMultiset<T>).innerMap)
            copy.setMultiplicity(t, b)
            return copy
        }

        fun <T> union(th: DafnyMultiset<out T>, other: DafnyMultiset<out T>): DafnyMultiset<T> {
            require(th != null) { "Precondition Violation" }
            require(other != null) { "Precondition Violation" }

            val u = DafnyMultiset((th as DafnyMultiset<T>).innerMap)
            for (entry in (other as DafnyMultiset<T>).innerMap.entries) {
                u.incrementMultiplicity(entry.key, entry.value)
            }
            return u
        }

        // Returns a DafnyMultiSet with multiplicities that are
        // max(this.multiplicity(e)-other.multiplicity(e), BigInteger.ZERO)
        fun <T> difference(th: DafnyMultiset<out T>, other: DafnyMultiset<out T>): DafnyMultiset<T> {
            require(th != null) { "Precondition Violation" }
            require(other != null) { "Precondition Violation" }

            val u = DafnyMultiset((th as DafnyMultiset<T>).innerMap)
            for (entry in (other as DafnyMultiset<T>).innerMap.entries) {
                val key = entry.key
                val m0 = multiplicity(u, key)
                val m1 = entry.value
                u.setMultiplicity(key, m0.subtract(m1))
            }
            return u
        }

        // Returns a DafnyMultiSet with multiplicities that are min(this.multiplicity(e), other.multiplicity(e))
        fun <T> intersection(th: DafnyMultiset<out T>, other: DafnyMultiset<out T>): DafnyMultiset<T> {
            require(th != null) { "Precondition Violation" }
            require(other != null) { "Precondition Violation" }

            val u = DafnyMultiset<T>()
            for (entry in (th as DafnyMultiset<T>).innerMap.entries) {
                val key = entry.key
                val m0 = entry.value
                val m1 = multiplicity(other as DafnyMultiset<T>, key)
                u.setMultiplicity(key, m0.min(m1))
            }
            return u
        }
    }
}
