// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

@file:Suppress("FunctionName", "UNCHECKED_CAST")

package dafny

/**
 * A wrapper for an array that may be of primitive type.  Essentially acts as
 * one "big box" for many primitives, as an alternative to putting each in their
 * own box.  Much faster than using reflection-based array operations to operate
 * on possibly-primitive arrays.
 *
 * This isn't used by generated Dafny code, which directly uses values of type
 * Object when the element type isn't known, relying on a [TypeDescriptor] passed
 * in.  It's much more pleasant to use, and more type-safe, than the bare
 * [TypeDescriptor] operations, however, so extern implementors may be interested.
 * It is also used to implement [DafnySequence].
 *
 * @param T The type of the elements in the array, or if that type is
 *          primitive, the boxed version of that type (e.g., [Integer] for int).
 */
class Array<T> private constructor(
    private val eltType: TypeDescriptor<T>,
    private val array: Any
) {

    fun elementType(): TypeDescriptor<T> = eltType

    fun unwrap(): Any = array

    fun get(index: Int): T = eltType.getArrayElement(array, index)

    fun set(index: Int, value: T) {
        eltType.setArrayElement(array, index, value)
    }

    fun length(): Int = eltType.getArrayLength(array)

    fun fill(value: T) {
        eltType.fillArray(array, value)
    }

    fun copy(): Array<T> = Array(eltType, eltType.cloneArray(array))

    fun copy(offset: Int, to: Array<T>, toOffset: Int, length: Int) {
        // Kotlin-Multiplatform equivalent of System.arraycopy, dispatched through the
        // element TypeDescriptor since `array` may be a primitive or reference array.
        eltType.copyArrayTo(this.array, offset, to.array, toOffset, length)
    }

    fun copyOfRange(lo: Int, hi: Int): Array<T> {
        val newArray = newArray(eltType, hi - lo)
        copy(lo, newArray, 0, hi - lo)
        return newArray
    }

    fun shallowEquals(other: Array<T>): Boolean =
        eltType.arrayShallowEquals(this.array, other.array)

    companion object {
        fun <T> newArray(eltType: TypeDescriptor<T>, length: Int): Array<T> =
            Array(eltType, eltType.newArray(length))

        fun <T> fromList(eltType: TypeDescriptor<T>, elements: List<T>): Array<T> =
            Array(eltType, eltType.toArray(elements))

        fun <T> wrap(eltType: TypeDescriptor<T>, array: Any): Array<T> =
            // The generated code sometimes passes a dafny.Array1 wrapper where a raw backing
            // store is expected (e.g. DafnySequence.fromRawArray(td, someArray1)); unwrap it
            // to its backing store so the element ops see a plain array, not the wrapper.
            Array(eltType, if (array is Array1<*>) array.elmts else array)

        // Note: We need the element type passed in here because otherwise the
        // actual type of the array might not be T[] but S[] where S is a subclass
        // of T.
        fun <T> wrap(eltType: TypeDescriptor<T>, array: kotlin.Array<T>): Array<T> =
            Array(eltType, array)

        fun wrap(array: ByteArray): Array<Byte> = Array(TypeDescriptor.BYTE, array)

        fun wrap(array: ShortArray): Array<Short> = Array(TypeDescriptor.SHORT, array)

        fun wrap(array: IntArray): Array<Int> = Array(TypeDescriptor.INT, array)

        fun wrap(array: LongArray): Array<Long> = Array(TypeDescriptor.LONG, array)

        fun wrap(array: BooleanArray): Array<Boolean> = Array(TypeDescriptor.BOOLEAN, array)

        fun wrap(array: CharArray): Array<Char> = Array(TypeDescriptor.CHAR, array)

        fun unwrap(array: Array<*>): Any = array.unwrap()

        fun <T> unwrapObjects(array: Array<T>): kotlin.Array<T> =
            array.array as kotlin.Array<T>

        fun unwrapBytes(array: Array<Byte>): ByteArray = array.array as ByteArray

        fun unwrapShorts(array: Array<Short>): ShortArray = array.array as ShortArray

        fun unwrapInts(array: Array<Int>): IntArray = array.array as IntArray

        fun unwrapLongs(array: Array<Long>): LongArray = array.array as LongArray

        fun unwrapBooleans(array: Array<Boolean>): BooleanArray = array.array as BooleanArray

        fun unwrapChars(array: Array<Char>): CharArray = array.array as CharArray
    }
}
