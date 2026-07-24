// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Tuple6<T0, T1, T2, T3, T4, T5>(private val _0: T0, private val _1: T1, private val _2: T2, private val _3: T3, private val _4: T4, private val _5: T5) {

    companion object {
        fun <T0, T1, T2, T3, T4, T5> _typeDescriptor(_td_T0: TypeDescriptor<T0>, _td_T1: TypeDescriptor<T1>, _td_T2: TypeDescriptor<T2>, _td_T3: TypeDescriptor<T3>, _td_T4: TypeDescriptor<T4>, _td_T5: TypeDescriptor<T5>): TypeDescriptor<Tuple6<T0, T1, T2, T3, T4, T5>> =
            TypeDescriptor.referenceWithInitializer { Default(_td_T0.defaultValue(), _td_T1.defaultValue(), _td_T2.defaultValue(), _td_T3.defaultValue(), _td_T4.defaultValue(), _td_T5.defaultValue()) } as TypeDescriptor<Tuple6<T0, T1, T2, T3, T4, T5>>
        fun <T0, T1, T2, T3, T4, T5> Default(_default_T0: T0, _default_T1: T1, _default_T2: T2, _default_T3: T3, _default_T4: T4, _default_T5: T5): Tuple6<T0, T1, T2, T3, T4, T5> = create(_default_T0, _default_T1, _default_T2, _default_T3, _default_T4, _default_T5)
        fun <T0, T1, T2, T3, T4, T5> create(_0: T0, _1: T1, _2: T2, _3: T3, _4: T4, _5: T5): Tuple6<T0, T1, T2, T3, T4, T5> = Tuple6(_0, _1, _2, _3, _4, _5)
    }

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other == null || this::class != other::class) return false
        val o = other as Tuple6<*, *, *, *, *, *>
        return this._0 == o._0 && this._1 == o._1 && this._2 == o._2 && this._3 == o._3 && this._4 == o._4 && this._5 == o._5
    }

    override fun hashCode(): Int {
        var hash = 5381L
        hash = ((hash shl 5) + hash) + 0
        hash = ((hash shl 5) + hash) + (this._0?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._1?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._2?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._3?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._4?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._5?.hashCode() ?: 0).toLong()
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
        sb.append(", ")
        sb.append(if (this._3 == null) "null" else this._3.toString())
        sb.append(", ")
        sb.append(if (this._4 == null) "null" else this._4.toString())
        sb.append(", ")
        sb.append(if (this._5 == null) "null" else this._5.toString())
        sb.append(")")
        return sb.toString()
    }

    fun dtor__0(): T0 = this._0

    fun dtor__1(): T1 = this._1

    fun dtor__2(): T2 = this._2

    fun dtor__3(): T3 = this._3

    fun dtor__4(): T4 = this._4

    fun dtor__5(): T5 = this._5
}
