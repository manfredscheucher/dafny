// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

class Array15<T>(
    private val elmtType: TypeDescriptor<T>,
    val dim0: Int, val dim1: Int, val dim2: Int, val dim3: Int, val dim4: Int, val dim5: Int, val dim6: Int, val dim7: Int, val dim8: Int, val dim9: Int, val dim10: Int, val dim11: Int, val dim12: Int, val dim13: Int, val dim14: Int,
    val elmts: kotlin.Array<Any?>
) {
    fun get(i0: Int, i1: Int, i2: Int, i3: Int, i4: Int, i5: Int, i6: Int, i7: Int, i8: Int, i9: Int, i10: Int, i11: Int, i12: Int, i13: Int, i14: Int): T = elmtType.getArrayElement(((((((((((((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3] as kotlin.Array<Any?>)[i4] as kotlin.Array<Any?>)[i5] as kotlin.Array<Any?>)[i6] as kotlin.Array<Any?>)[i7] as kotlin.Array<Any?>)[i8] as kotlin.Array<Any?>)[i9] as kotlin.Array<Any?>)[i10] as kotlin.Array<Any?>)[i11] as kotlin.Array<Any?>)[i12] as kotlin.Array<Any?>)[i13], i14)

    fun set(i0: Int, i1: Int, i2: Int, i3: Int, i4: Int, i5: Int, i6: Int, i7: Int, i8: Int, i9: Int, i10: Int, i11: Int, i12: Int, i13: Int, i14: Int, value: T) { elmtType.setArrayElement(((((((((((((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3] as kotlin.Array<Any?>)[i4] as kotlin.Array<Any?>)[i5] as kotlin.Array<Any?>)[i6] as kotlin.Array<Any?>)[i7] as kotlin.Array<Any?>)[i8] as kotlin.Array<Any?>)[i9] as kotlin.Array<Any?>)[i10] as kotlin.Array<Any?>)[i11] as kotlin.Array<Any?>)[i12] as kotlin.Array<Any?>)[i13], i14, value) }

    fun fill(z: T) {
        for (i0 in 0 until dim0) {
            for (i1 in 0 until dim1) {
                for (i2 in 0 until dim2) {
                    for (i3 in 0 until dim3) {
                        for (i4 in 0 until dim4) {
                            for (i5 in 0 until dim5) {
                                for (i6 in 0 until dim6) {
                                    for (i7 in 0 until dim7) {
                                        for (i8 in 0 until dim8) {
                                            for (i9 in 0 until dim9) {
                                                for (i10 in 0 until dim10) {
                                                    for (i11 in 0 until dim11) {
                                                        for (i12 in 0 until dim12) {
                                                            for (i13 in 0 until dim13) {
                                                                elmtType.fillArray(((((((((((((((elmts)[i0] as kotlin.Array<Any?>)[i1] as kotlin.Array<Any?>)[i2] as kotlin.Array<Any?>)[i3] as kotlin.Array<Any?>)[i4] as kotlin.Array<Any?>)[i5] as kotlin.Array<Any?>)[i6] as kotlin.Array<Any?>)[i7] as kotlin.Array<Any?>)[i8] as kotlin.Array<Any?>)[i9] as kotlin.Array<Any?>)[i10] as kotlin.Array<Any?>)[i11] as kotlin.Array<Any?>)[i12] as kotlin.Array<Any?>)[i13], z)
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    fun fillThenReturn(z: T): Array15<T> { fill(z); return this }

    companion object {
        private val TYPE: TypeDescriptor<Array15<*>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Array15<*>>
        fun <T> _typeDescriptor(): TypeDescriptor<Array15<T>> = TYPE as TypeDescriptor<Array15<T>>
    }
}
