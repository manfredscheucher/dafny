// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Tuple2<T0, T1>(private val _0: T0, private val _1: T1) {

    companion object {
        fun <T0, T1> _typeDescriptor(_td_T0: TypeDescriptor<T0>, _td_T1: TypeDescriptor<T1>): TypeDescriptor<Tuple2<T0, T1>> =
            TypeDescriptor.referenceWithInitializer { Default(_td_T0.defaultValue(), _td_T1.defaultValue()) } as TypeDescriptor<Tuple2<T0, T1>>
        fun <T0, T1> Default(_default_T0: T0, _default_T1: T1): Tuple2<T0, T1> = create(_default_T0, _default_T1)
        fun <T0, T1> create(_0: T0, _1: T1): Tuple2<T0, T1> = Tuple2(_0, _1)
    }

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other == null || this::class != other::class) return false
        val o = other as Tuple2<*, *>
        return this._0 == o._0 && this._1 == o._1
    }

    override fun hashCode(): Int {
        var hash = 5381L
        hash = ((hash shl 5) + hash) + 0
        hash = ((hash shl 5) + hash) + (this._0?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._1?.hashCode() ?: 0).toLong()
        return hash.toInt()
    }

    override fun toString(): String {
        val sb = StringBuilder()
        sb.append("(")
        sb.append(if (this._0 == null) "null" else this._0.toString())
        sb.append(", ")
        sb.append(if (this._1 == null) "null" else this._1.toString())
        sb.append(")")
        return sb.toString()
    }

    fun dtor__0(): T0 = this._0

    fun dtor__1(): T1 = this._1
}
