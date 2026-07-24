@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "NAME_SHADOWING", "FunctionName")

// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny

// A class that is equivalent to the implementation of Set in Dafny.
// Covariant (`out T`) to match Dafny's covariant sets; T-in-input positions are marked
// @UnsafeVariance (safe: those are internal/construction operations). innerSet is `internal`
// (not private) so companion operations can read another instance's backing set.
class DafnySet<out T> {
    internal val innerSet: MutableSet<@UnsafeVariance T>

    constructor() {
        innerSet = HashSet()
    }

    constructor(s: Set<@UnsafeVariance T>) {
        require(s != null) { "Precondition Violation" }
        innerSet = HashSet(s)
    }

    constructor(c: Collection<@UnsafeVariance T>) {
        require(c != null) { "Precondition Violation" }
        innerSet = HashSet(c)
    }

    constructor(other: DafnySet<T>) {
        require(other != null) { "Precondition Violation" }
        innerSet = HashSet(other.innerSet)
    }

    constructor(l: List<@UnsafeVariance T>) {
        require(l != null) { "Precondition Violation" }
        innerSet = HashSet(l)
    }

    // Determines if the current object is a subset of the DafnySet passed in. Requires that the input DafnySet is not
    // null.
    fun isSubsetOf(other: DafnySet<*>): Boolean {
        require(other != null) { "Precondition Violation" }
        return other.containsAll(this)
    }

    // Determines if the current object is a proper subset of the DafnySet passed in. Requires that the input DafnySet
    // is not null.
    fun isProperSubsetOf(other: DafnySet<*>): Boolean {
        require(other != null) { "Precondition Violation" }
        return isSubsetOf(other) && size() < other.size()
    }

    fun contains(t: Any?): Boolean {
        // No null-precondition check: the Java runtime used `assert` (disabled by default),
        // so `contains(null)` returned false rather than throwing. Dafny sets over nullable
        // element types may legitimately be queried with null.
        // innerSet is covariant (MutableSet<out T>); view it as Set<Any?> for the lookup so
        // type inference doesn't need to pin T (needed for the JS/native compilers).
        @Suppress("UNCHECKED_CAST")
        return (innerSet as Set<Any?>).contains(t)
    }

    fun <U> disjoint(other: DafnySet<out U>): Boolean {
        require(other != null) { "Precondition Violation" }
        for (u in other.innerSet) {
            if (contains(u)) return false
        }
        return true
    }

    fun containsAll(other: DafnySet<*>): Boolean {
        require(other != null) { "Precondition Violation" }
        return innerSet.containsAll(other.innerSet)
    }

    fun size(): Int {
        return innerSet.size
    }

    fun cardinalityInt(): Int {
        return size()
    }

    fun isEmpty(): Boolean {
        return innerSet.isEmpty()
    }

    fun add(t: @UnsafeVariance T): Boolean {
        // No null-precondition check (see `contains`): matches the Java runtime's disabled assert.
        return innerSet.add(t)
    }

    fun remove(t: @UnsafeVariance T): Boolean {
        return innerSet.remove(t)
    }

    fun removeAll(other: DafnySet<@UnsafeVariance T>): Boolean {
        require(other != null) { "Precondition Violation" }
        return innerSet.removeAll(other.innerSet)
    }

    fun addAll(other: DafnySet<@UnsafeVariance T>): Boolean {
        require(other != null) { "Precondition Violation" }
        return innerSet.addAll(other.innerSet)
    }

    fun AllSubsets(): Collection<DafnySet<T>> {
        // Start by putting all set elements into a list, but don't include null
        val elmts = ArrayList<T>()
        elmts.addAll(innerSet)
        val n = elmts.size
        var s: DafnySet<T>
        val r = HashSet<DafnySet<T>>()
        for (i in 0 until (1 shl n)) {
            s = DafnySet()
            var m = 1 // m is used to check set bit in binary representation.
            // Build current subset
            var j = 0
            while (j < n) {
                if ((i and m) > 0) {
                    s.add(elmts[j])
                }
                j++
                m = m shl 1
            }
            r.add(s)
        }
        return r
    }

    override fun equals(obj: Any?): Boolean {
        if (this === obj) return true
        if (obj == null) return false
        if (this::class != obj::class) return false
        val o = obj as DafnySet<*>
        return containsAll(o) && o.containsAll(this)
    }

    override fun hashCode(): Int {
        return innerSet.hashCode()
    }

    override fun toString(): String {
        var s = "{"
        var sep = ""
        for (elem in innerSet) {
            s += sep + Helpers.toString(elem)
            sep = ", "
        }
        return "$s}"
    }

    fun asDafnyMultiset(): DafnyMultiset<T> {
        return DafnyMultiset(innerSet)
    }

    fun Elements(): Set<T> {
        return innerSet
    }

    companion object {
        fun <T> of(vararg elements: T): DafnySet<T> {
            return DafnySet(listOf(*elements))
        }

        private val EMPTY: DafnySet<Any?> = of<Any?>()

        fun <T> empty(): DafnySet<T> {
            // Safe because immutable
            return EMPTY as DafnySet<T>
        }

        fun <T> _typeDescriptor(elementType: TypeDescriptor<T>): TypeDescriptor<DafnySet<out T>> {
            // Fudge the type parameter; it's not great, but it's safe because
            // (for now) type descriptors are only used for default values
            return TypeDescriptor.referenceWithDefault(empty<T>())
        }

        fun <T> union(th: DafnySet<out T>, other: DafnySet<out T>): DafnySet<T> {
            require(th != null) { "Precondition Violation" }
            require(other != null) { "Precondition Violation" }

            return if (th.isEmpty()) {
                other as DafnySet<T>
            } else if (other.isEmpty()) {
                th as DafnySet<T>
            } else {
                val u = DafnySet(other as DafnySet<T>)
                u.addAll(th as DafnySet<T>)
                u
            }
        }

        // Returns a DafnySet containing elements only found in the current DafnySet
        fun <T> difference(th: DafnySet<out T>, other: DafnySet<out T>): DafnySet<T> {
            require(th != null) { "Precondition Violation" }
            require(other != null) { "Precondition Violation" }
            val u = DafnySet(th as DafnySet<T>)
            u.removeAll(other as DafnySet<T>)
            return u
        }

        fun <T> intersection(th: DafnySet<out T>, other: DafnySet<out T>): DafnySet<T> {
            require(th != null) { "Precondition Violation" }
            require(other != null) { "Precondition Violation" }
            val u = DafnySet<T>()
            for (ele in (th as DafnySet<T>).innerSet) {
                if (other.contains(ele)) u.add(ele)
            }
            return u
        }
    }
}
