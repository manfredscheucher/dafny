// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Tuple3<T0, T1, T2>(private val _0: T0, private val _1: T1, private val _2: T2) {

    companion object {
        fun <T0, T1, T2> _typeDescriptor(_td_T0: TypeDescriptor<T0>, _td_T1: TypeDescriptor<T1>, _td_T2: TypeDescriptor<T2>): TypeDescriptor<Tuple3<T0, T1, T2>> =
            TypeDescriptor.referenceWithInitializer { Default(_td_T0.defaultValue(), _td_T1.defaultValue(), _td_T2.defaultValue()) } as TypeDescriptor<Tuple3<T0, T1, T2>>
        fun <T0, T1, T2> Default(_default_T0: T0, _default_T1: T1, _default_T2: T2): Tuple3<T0, T1, T2> = create(_default_T0, _default_T1, _default_T2)
        fun <T0, T1, T2> create(_0: T0, _1: T1, _2: T2): Tuple3<T0, T1, T2> = Tuple3(_0, _1, _2)
    }

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other == null || this::class != other::class) return false
        val o = other as Tuple3<*, *, *>
        return this._0 == o._0 && this._1 == o._1 && this._2 == o._2
    }

    override fun hashCode(): Int {
        var hash = 5381L
        hash = ((hash shl 5) + hash) + 0
        hash = ((hash shl 5) + hash) + (this._0?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._1?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._2?.hashCode() ?: 0).toLong()
        return hash.toInt()
    }

    override fun toString(): String {
        val sb = StringBuilder()
        sb.append("(")
        sb.append(if (this._0 == null) "null" else this._0.toString())
        sb.append(", ")
        sb.append(if (this._1 == null) "null" else this._1.toString())
        sb.append(", ")
        sb.append(if (this._2 == null) "null" else this._2.toString())
        sb.append(")")
        return sb.toString()
    }

    fun dtor__0(): T0 = this._0

    fun dtor__1(): T1 = this._1

    fun dtor__2(): T2 = this._2
}
