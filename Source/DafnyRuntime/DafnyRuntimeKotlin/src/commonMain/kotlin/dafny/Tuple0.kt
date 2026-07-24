// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Tuple0 {

    companion object {
        private val _TYPE: TypeDescriptor<Tuple0> =
            TypeDescriptor.referenceWithInitializer { Default() } as TypeDescriptor<Tuple0>
        fun _typeDescriptor(): TypeDescriptor<Tuple0> = _TYPE
        fun Default(): Tuple0 = create()
        fun create(): Tuple0 = Tuple0()
    }

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other == null || this::class != other::class) return false
        return true
    }

    override fun hashCode(): Int {
        var hash = 5381L
        hash = ((hash shl 5) + hash) + 0
        return hash.toInt()
    }

    override fun toString(): String {
        val sb = StringBuilder()
        sb.append("(")
        sb.append(")")
        return sb.toString()
    }
}
