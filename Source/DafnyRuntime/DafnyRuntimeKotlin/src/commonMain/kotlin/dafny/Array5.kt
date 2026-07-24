// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Array5<T>(
    private val elmtType: TypeDescriptor<T>,
    val dim0: Int, val dim1: Int, val dim2: Int, val dim3: Int, val dim4: Int,
    val elmts: kotlin.Array<Any?>
) {
    fun get(i0: Int, i1: Int, i2: Int, i3: Int, i4: Int): T = elmtType.getArrayElement(((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3], i4)

    fun set(i0: Int, i1: Int, i2: Int, i3: Int, i4: Int, value: T) { elmtType.setArrayElement(((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3], i4, value) }

    fun fill(z: T) {
        for (i0 in 0 until dim0) {
            for (i1 in 0 until dim1) {
                for (i2 in 0 until dim2) {
                    for (i3 in 0 until dim3) {
                        elmtType.fillArray(((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3], z)
                    }
                }
            }
        }
    }

    fun fillThenReturn(z: T): Array5<T> { fill(z); return this }

    companion object {
        private val TYPE: TypeDescriptor<Array5<*>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Array5<*>>
        fun <T> _typeDescriptor(): TypeDescriptor<Array5<T>> = TYPE as TypeDescriptor<Array5<T>>
    }
}
