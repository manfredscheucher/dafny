// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Array2<T>(
    private val elmtType: TypeDescriptor<T>,
    val dim0: Int, val dim1: Int,
    val elmts: kotlin.Array<Any?>
) {
    fun get(i0: Int, i1: Int): T = elmtType.getArrayElement((elmts)[i0], i1)

    fun set(i0: Int, i1: Int, value: T) { elmtType.setArrayElement((elmts)[i0], i1, value) }

    fun fill(z: T) {
        for (i0 in 0 until dim0) {
            elmtType.fillArray((elmts)[i0], z)
        }
    }

    fun fillThenReturn(z: T): Array2<T> { fill(z); return this }

    companion object {
        private val TYPE: TypeDescriptor<Array2<*>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Array2<*>>
        fun <T> _typeDescriptor(): TypeDescriptor<Array2<T>> = TYPE as TypeDescriptor<Array2<T>>
    }
}
