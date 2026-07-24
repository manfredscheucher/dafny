// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Tuple1<T0>(private val _0: T0) {

    companion object {
        fun <T0> _typeDescriptor(_td_T0: TypeDescriptor<T0>): TypeDescriptor<Tuple1<T0>> =
            TypeDescriptor.referenceWithInitializer { Default(_td_T0.defaultValue()) } as TypeDescriptor<Tuple1<T0>>
        fun <T0> Default(_default_T0: T0): Tuple1<T0> = create(_default_T0)
        fun <T0> create(_0: T0): Tuple1<T0> = Tuple1(_0)
    }

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other == null || this::class != other::class) return false
        val o = other as Tuple1<*>
        return this._0 == o._0
    }

    override fun hashCode(): Int {
        var hash = 5381L
        hash = ((hash shl 5) + hash) + 0
        hash = ((hash shl 5) + hash) + (this._0?.hashCode() ?: 0).toLong()
        return hash.toInt()
    }

    override fun toString(): String {
        val sb = StringBuilder()
        sb.append("(")
        sb.append(if (this._0 == null) "null" else this._0.toString())
        sb.append(")")
        return sb.toString()
    }

    fun dtor__0(): T0 = this._0
}
