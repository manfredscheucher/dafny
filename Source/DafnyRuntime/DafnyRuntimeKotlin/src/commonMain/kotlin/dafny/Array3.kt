// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Array3<T>(
    private val elmtType: TypeDescriptor<T>,
    val dim0: Int, val dim1: Int, val dim2: Int,
    val elmts: kotlin.Array<Any?>
) {
    fun get(i0: Int, i1: Int, i2: Int): T = elmtType.getArrayElement(((elmts)[i0] as kotlin.Array<Any?>)[i1], i2)

    fun set(i0: Int, i1: Int, i2: Int, value: T) { elmtType.setArrayElement(((elmts)[i0] as kotlin.Array<Any?>)[i1], i2, value) }

    fun fill(z: T) {
        for (i0 in 0 until dim0) {
            for (i1 in 0 until dim1) {
                elmtType.fillArray(((elmts)[i0] as kotlin.Array<Any?>)[i1], z)
            }
        }
    }

    fun fillThenReturn(z: T): Array3<T> { fill(z); return this }

    companion object {
        private val TYPE: TypeDescriptor<Array3<*>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Array3<*>>
        fun <T> _typeDescriptor(): TypeDescriptor<Array3<T>> = TYPE as TypeDescriptor<Array3<T>>
    }
}
