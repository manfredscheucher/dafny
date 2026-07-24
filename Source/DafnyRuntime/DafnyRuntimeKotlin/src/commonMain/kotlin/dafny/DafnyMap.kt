@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "NAME_SHADOWING", "FunctionName")

// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny


// Covariant (`out K, out V`) to match Dafny's covariant maps; K/V-in-input positions are
// @UnsafeVariance, and innerMap is `internal` so companion operations can read another
// instance's backing map.
class DafnyMap<out K, out V> {
    internal var innerMap: MutableMap<@UnsafeVariance K, @UnsafeVariance V>

    constructor() {
        innerMap = HashMap()
    }

    private constructor(innerMap: HashMap<@UnsafeVariance K, @UnsafeVariance V>) {
        this.innerMap = innerMap
    }

    constructor(m: Map<@UnsafeVariance K, @UnsafeVariance V>) {
        innerMap = HashMap()
        m.forEach { (k, v) -> innerMap[k] = v }
    }

    fun contains(t: Any?): Boolean {
        return innerMap.containsKey(t)
    }

    override fun equals(obj: Any?): Boolean {
        if (this === obj) return true
        if (obj == null) return false
        if (this::class != obj::class) return false
        val o = obj as DafnyMap<*, *>
        return innerMap == o.innerMap
    }

    override fun hashCode(): Int {
        return innerMap.hashCode()
    }

    override fun toString(): String {
        var s = "map["
        var sep = ""
        for (entry in innerMap.entries) {
            s += sep + Helpers.toString(entry.key) + " := " + Helpers.toString(entry.value)
            sep = ", "
        }
        return "$s]"
    }

    fun forEach(action: (K, V) -> Unit) {
        innerMap.forEach { (k, v) -> action(k, v) }
    }

    fun size(): Int {
        return innerMap.size
    }

    fun cardinalityInt(): Int {
        return size()
    }

    fun isEmpty(): Boolean {
        return innerMap.isEmpty()
    }

    fun get(key: Any?): V? {
        return innerMap[key]
    }

    fun keySet(): DafnySet<K> {
        return DafnySet(innerMap.keys)
    }

    fun valueSet(): DafnySet<V> {
        return DafnySet(innerMap.values)
    }

    // Until tuples (and other datatypes) are compiled with type-argument variance, the following
    // method takes type parameters <KK, VV>. The expectation is that <K, V> is <? extends KK, ? extends VV>.
    fun <KK, VV> entrySet(): DafnySet<out Tuple2<KK, VV>> {
        val list = ArrayList<Tuple2<K, V>>()
        for (entry in innerMap.entries) {
            list.add(Tuple2(entry.key, entry.value))
        }
        return (DafnySet(list) as Any) as DafnySet<out Tuple2<KK, VV>>
    }

    companion object {
        fun <K, V> empty(): DafnyMap<K, V> {
            return DafnyMap()
        }

        fun <K, V> fromElements(vararg pairs: Tuple2<K, V>): DafnyMap<K, V> {
            val result = DafnyMap<K, V>()
            for (pair in pairs) {
                result.innerMap[pair.dtor__0()] = pair.dtor__1()
            }
            return result
        }

        fun <K, V> _typeDescriptor(
            keyType: TypeDescriptor<K>, valueType: TypeDescriptor<V>
        ): TypeDescriptor<DafnyMap<out K, out V>> {
            // Fudge the type parameters; it's not great, but it's safe because
            // (for now) type descriptors are only used for default values
            return TypeDescriptor.referenceWithDefault(empty<K, V>())
        }

        fun <K, V> update(th: DafnyMap<out K, out V>, k: K, v: V): DafnyMap<K, V> {
            val copy = HashMap<K, V>(th.innerMap as Map<K, V>)
            copy[k] = v
            val r = DafnyMap<K, V>()
            r.innerMap = copy
            return r
        }

        fun <K, V> merge(th: DafnyMap<out K, out V>, other: DafnyMap<out K, out V>): DafnyMap<out K, out V> {

            if (th.isEmpty()) {
                return other
            } else if (other.isEmpty()) {
                return th
            }

            val m = HashMap<K, V>(other.innerMap as Map<K, V>)
            (th as DafnyMap<K, V>).forEach { k, v ->
                if (!m.containsKey(k)) {
                    m[k] = v
                }
            }
            return DafnyMap(m)
        }

        fun <K, V> subtract(th: DafnyMap<out K, out V>, keys: DafnySet<out K>): DafnyMap<out K, out V> {

            if (th.isEmpty() || keys.isEmpty()) {
                return th
            }

            val m = HashMap<K, V>(th.innerMap as Map<K, V>)
            for (k in keys.Elements()) {
                m.remove(k)
            }
            return DafnyMap(m)
        }
    }
}
