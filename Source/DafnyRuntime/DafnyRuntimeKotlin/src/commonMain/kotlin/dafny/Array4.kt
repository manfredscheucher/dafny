// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Array4<T>(
    private val elmtType: TypeDescriptor<T>,
    val dim0: Int, val dim1: Int, val dim2: Int, val dim3: Int,
    val elmts: kotlin.Array<Any?>
) {
    fun get(i0: Int, i1: Int, i2: Int, i3: Int): T = elmtType.getArrayElement((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2], i3)

    fun set(i0: Int, i1: Int, i2: Int, i3: Int, value: T) { elmtType.setArrayElement((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2], i3, value) }

    fun fill(z: T) {
        for (i0 in 0 until dim0) {
            for (i1 in 0 until dim1) {
                for (i2 in 0 until dim2) {
                    elmtType.fillArray((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2], z)
                }
            }
        }
    }

    fun fillThenReturn(z: T): Array4<T> { fill(z); return this }

    companion object {
        private val TYPE: TypeDescriptor<Array4<*>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Array4<*>>
        fun <T> _typeDescriptor(): TypeDescriptor<Array4<T>> = TYPE as TypeDescriptor<Array4<T>>
    }
}
