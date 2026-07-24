// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Array1<T>(
    private val elmtType: TypeDescriptor<T>,
    val dim0: Int,
    val elmts: Any
) {
    operator fun get(i0: Int): T = elmtType.getArrayElement(elmts, i0)

    operator fun set(i0: Int, value: T) { elmtType.setArrayElement(elmts, i0, value) }

    fun fill(z: T) {
        elmtType.fillArray(elmts, z)
    }

    fun fillThenReturn(z: T): Array1<T> { fill(z); return this }

    companion object {
        private val TYPE: TypeDescriptor<Array1<*>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Array1<*>>
        fun <T> _typeDescriptor(): TypeDescriptor<Array1<T>> = TYPE as TypeDescriptor<Array1<T>>
    }
}
