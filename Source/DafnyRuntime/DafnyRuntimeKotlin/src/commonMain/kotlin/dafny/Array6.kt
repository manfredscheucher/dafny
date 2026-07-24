// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Array6<T>(
    private val elmtType: TypeDescriptor<T>,
    val dim0: Int, val dim1: Int, val dim2: Int, val dim3: Int, val dim4: Int, val dim5: Int,
    val elmts: kotlin.Array<Any?>
) {
    fun get(i0: Int, i1: Int, i2: Int, i3: Int, i4: Int, i5: Int): T = elmtType.getArrayElement((((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3] as kotlin.Array<Any?>)[i4], i5)

    fun set(i0: Int, i1: Int, i2: Int, i3: Int, i4: Int, i5: Int, value: T) { elmtType.setArrayElement((((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3] as kotlin.Array<Any?>)[i4], i5, value) }

    fun fill(z: T) {
        for (i0 in 0 until dim0) {
            for (i1 in 0 until dim1) {
                for (i2 in 0 until dim2) {
                    for (i3 in 0 until dim3) {
                        for (i4 in 0 until dim4) {
                            elmtType.fillArray((((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3] as kotlin.Array<Any?>)[i4], z)
                        }
                    }
                }
            }
        }
    }

    fun fillThenReturn(z: T): Array6<T> { fill(z); return this }

    companion object {
        private val TYPE: TypeDescriptor<Array6<*>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Array6<*>>
        fun <T> _typeDescriptor(): TypeDescriptor<Array6<T>> = TYPE as TypeDescriptor<Array6<T>>
    }
}
