// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Array7<T>(
    private val elmtType: TypeDescriptor<T>,
    val dim0: Int, val dim1: Int, val dim2: Int, val dim3: Int, val dim4: Int, val dim5: Int, val dim6: Int,
    val elmts: kotlin.Array<Any?>
) {
    fun get(i0: Int, i1: Int, i2: Int, i3: Int, i4: Int, i5: Int, i6: Int): T = elmtType.getArrayElement(((((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3] as kotlin.Array<Any?>)[i4] as kotlin.Array<Any?>)[i5], i6)

    fun set(i0: Int, i1: Int, i2: Int, i3: Int, i4: Int, i5: Int, i6: Int, value: T) { elmtType.setArrayElement(((((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3] as kotlin.Array<Any?>)[i4] as kotlin.Array<Any?>)[i5], i6, value) }

    fun fill(z: T) {
        for (i0 in 0 until dim0) {
            for (i1 in 0 until dim1) {
                for (i2 in 0 until dim2) {
                    for (i3 in 0 until dim3) {
                        for (i4 in 0 until dim4) {
                            for (i5 in 0 until dim5) {
                                elmtType.fillArray(((((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3] as kotlin.Array<Any?>)[i4] as kotlin.Array<Any?>)[i5], z)
                            }
                        }
                    }
                }
            }
        }
    }

    fun fillThenReturn(z: T): Array7<T> { fill(z); return this }

    companion object {
        private val TYPE: TypeDescriptor<Array7<*>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Array7<*>>
        fun <T> _typeDescriptor(): TypeDescriptor<Array7<T>> = TYPE as TypeDescriptor<Array7<T>>
    }
}
