// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Tuple19<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>(private val _0: T0, private val _1: T1, private val _2: T2, private val _3: T3, private val _4: T4, private val _5: T5, private val _6: T6, private val _7: T7, private val _8: T8, private val _9: T9, private val _10: T10, private val _11: T11, private val _12: T12, private val _13: T13, private val _14: T14, private val _15: T15, private val _16: T16, private val _17: T17, private val _18: T18) {

    companion object {
        fun <T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18> _typeDescriptor(_td_T0: TypeDescriptor<T0>, _td_T1: TypeDescriptor<T1>, _td_T2: TypeDescriptor<T2>, _td_T3: TypeDescriptor<T3>, _td_T4: TypeDescriptor<T4>, _td_T5: TypeDescriptor<T5>, _td_T6: TypeDescriptor<T6>, _td_T7: TypeDescriptor<T7>, _td_T8: TypeDescriptor<T8>, _td_T9: TypeDescriptor<T9>, _td_T10: TypeDescriptor<T10>, _td_T11: TypeDescriptor<T11>, _td_T12: TypeDescriptor<T12>, _td_T13: TypeDescriptor<T13>, _td_T14: TypeDescriptor<T14>, _td_T15: TypeDescriptor<T15>, _td_T16: TypeDescriptor<T16>, _td_T17: TypeDescriptor<T17>, _td_T18: TypeDescriptor<T18>): TypeDescriptor<Tuple19<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>> =
            TypeDescriptor.referenceWithInitializer { Default(_td_T0.defaultValue(), _td_T1.defaultValue(), _td_T2.defaultValue(), _td_T3.defaultValue(), _td_T4.defaultValue(), _td_T5.defaultValue(), _td_T6.defaultValue(), _td_T7.defaultValue(), _td_T8.defaultValue(), _td_T9.defaultValue(), _td_T10.defaultValue(), _td_T11.defaultValue(), _td_T12.defaultValue(), _td_T13.defaultValue(), _td_T14.defaultValue(), _td_T15.defaultValue(), _td_T16.defaultValue(), _td_T17.defaultValue(), _td_T18.defaultValue()) } as TypeDescriptor<Tuple19<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>>
        fun <T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18> Default(_default_T0: T0, _default_T1: T1, _default_T2: T2, _default_T3: T3, _default_T4: T4, _default_T5: T5, _default_T6: T6, _default_T7: T7, _default_T8: T8, _default_T9: T9, _default_T10: T10, _default_T11: T11, _default_T12: T12, _default_T13: T13, _default_T14: T14, _default_T15: T15, _default_T16: T16, _default_T17: T17, _default_T18: T18): Tuple19<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18> = create(_default_T0, _default_T1, _default_T2, _default_T3, _default_T4, _default_T5, _default_T6, _default_T7, _default_T8, _default_T9, _default_T10, _default_T11, _default_T12, _default_T13, _default_T14, _default_T15, _default_T16, _default_T17, _default_T18)
        fun <T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18> create(_0: T0, _1: T1, _2: T2, _3: T3, _4: T4, _5: T5, _6: T6, _7: T7, _8: T8, _9: T9, _10: T10, _11: T11, _12: T12, _13: T13, _14: T14, _15: T15, _16: T16, _17: T17, _18: T18): Tuple19<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18> = Tuple19(_0, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14, _15, _16, _17, _18)
    }

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other == null || this::class != other::class) return false
        val o = other as Tuple19<*, *, *, *, *, *, *, *, *, *, *, *, *, *, *, *, *, *, *>
        return this._0 == o._0 && this._1 == o._1 && this._2 == o._2 && this._3 == o._3 && this._4 == o._4 && this._5 == o._5 && this._6 == o._6 && this._7 == o._7 && this._8 == o._8 && this._9 == o._9 && this._10 == o._10 && this._11 == o._11 && this._12 == o._12 && this._13 == o._13 && this._14 == o._14 && this._15 == o._15 && this._16 == o._16 && this._17 == o._17 && this._18 == o._18
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
        hash = ((hash shl 5) + hash) + (this._6?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._7?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._8?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._9?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._10?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._11?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._12?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._13?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._14?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._15?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._16?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._17?.hashCode() ?: 0).toLong()
        hash = ((hash shl 5) + hash) + (this._18?.hashCode() ?: 0).toLong()
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
        sb.append(", ")
        sb.append(if (this._6 == null) "null" else this._6.toString())
        sb.append(", ")
        sb.append(if (this._7 == null) "null" else this._7.toString())
        sb.append(", ")
        sb.append(if (this._8 == null) "null" else this._8.toString())
        sb.append(", ")
        sb.append(if (this._9 == null) "null" else this._9.toString())
        sb.append(", ")
        sb.append(if (this._10 == null) "null" else this._10.toString())
        sb.append(", ")
        sb.append(if (this._11 == null) "null" else this._11.toString())
        sb.append(", ")
        sb.append(if (this._12 == null) "null" else this._12.toString())
        sb.append(", ")
        sb.append(if (this._13 == null) "null" else this._13.toString())
        sb.append(", ")
        sb.append(if (this._14 == null) "null" else this._14.toString())
        sb.append(", ")
        sb.append(if (this._15 == null) "null" else this._15.toString())
        sb.append(", ")
        sb.append(if (this._16 == null) "null" else this._16.toString())
        sb.append(", ")
        sb.append(if (this._17 == null) "null" else this._17.toString())
        sb.append(", ")
        sb.append(if (this._18 == null) "null" else this._18.toString())
        sb.append(")")
        return sb.toString()
    }

    fun dtor__0(): T0 = this._0

    fun dtor__1(): T1 = this._1

    fun dtor__2(): T2 = this._2

    fun dtor__3(): T3 = this._3

    fun dtor__4(): T4 = this._4

    fun dtor__5(): T5 = this._5

    fun dtor__6(): T6 = this._6

    fun dtor__7(): T7 = this._7

    fun dtor__8(): T8 = this._8

    fun dtor__9(): T9 = this._9

    fun dtor__10(): T10 = this._10

    fun dtor__11(): T11 = this._11

    fun dtor__12(): T12 = this._12

    fun dtor__13(): T13 = this._13

    fun dtor__14(): T14 = this._14

    fun dtor__15(): T15 = this._15

    fun dtor__16(): T16 = this._16

    fun dtor__17(): T17 = this._17

    fun dtor__18(): T18 = this._18
}
