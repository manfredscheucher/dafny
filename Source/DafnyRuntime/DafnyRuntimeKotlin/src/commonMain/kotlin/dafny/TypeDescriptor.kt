// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

@file:Suppress("UNCHECKED_CAST", "NAME_SHADOWING")

package dafny

// Runtime type descriptor for the Dafny Kotlin target.
//
// This is a REFLECTION-FREE, Kotlin-Multiplatform-friendly design: a TypeDescriptor
// carries the operations it needs as overridable methods (default value, array
// element access, array creation, …) rather than a `java.lang.Class` token it would
// reflect over. Nothing here references java.*.
//
// - Primitive-element descriptors (Byte/Short/Int/Long/Boolean/Char/CodePoint) use
//   Kotlin's typed arrays (ByteArray, IntArray, …) directly.
// - Reference-element descriptors back their arrays with a plain Array<Any?>; Dafny
//   boxes reference values, so no per-element class token is needed. `isInstance` is a
//   caller-supplied predicate (commonly `{ it is Foo }`) instead of `Class.isInstance`.
abstract class TypeDescriptor<T> {

    abstract fun defaultValue(): T

    abstract fun isInstance(obj: Any?): Boolean

    abstract fun arrayType(): TypeDescriptor<*>

    // Create a 1-D backing array of the given length (fast).
    abstract fun newArray(length: Int): Any

    // Create an N-D backing array. Only dims.size >= 1 matters; the innermost is a typed
    // array, the outer dimensions are Array<Any?> of nested arrays.
    open fun newArray(vararg dims: Int): Any {
        return newArrayRec(dims, 0)
    }

    private fun newArrayRec(dims: IntArray, i: Int): Any {
        if (i == dims.size - 1) {
            return newArray(dims[i])
        }
        val outer = arrayOfNulls<Any?>(dims[i])
        for (k in 0 until dims[i]) {
            outer[k] = newArrayRec(dims, i + 1)
        }
        return outer
    }

    abstract fun getArrayElement(array: Any?, index: Int): T

    abstract fun setArrayElement(array: Any?, index: Int, value: T)

    abstract fun getArrayLength(array: Any?): Int

    abstract fun cloneArray(array: Any?): Any

    abstract fun fillArray(array: Any?, value: T)

    fun fillThenReturnArray(array: Any?, value: T): Any? {
        fillArray(array, value)
        return array
    }

    abstract fun copyArrayTo(src: Any?, srcPos: Int, dest: Any?, destPos: Int, length: Int)

    abstract fun arrayShallowEquals(array1: Any?, array2: Any?): Boolean

    open fun toArray(coll: Collection<T>): Any {
        val arr = newArray(coll.size)
        var i = 0
        for (elt in coll) {
            setArrayElement(arr, i++, elt)
        }
        return arr
    }

    override fun toString(): String = "TypeDescriptor"

    fun interface Initializer<T> {
        // Dafny class/arrow default values are null, so the initializer may return null.
        fun defaultValue(): T?
    }

    // Descriptor for reference (boxed) element types. Arrays are Array<Any?>.
    private class ReferenceType<T>(
        private val initializer: Initializer<T>,
        private val instanceCheck: (Any?) -> Boolean
    ) : TypeDescriptor<T>() {
        private var arrayType: TypeDescriptor<*>? = null

        override fun defaultValue(): T = initializer.defaultValue() as T

        override fun isInstance(obj: Any?): Boolean = instanceCheck(obj)

        override fun arrayType(): TypeDescriptor<*> {
            if (arrayType == null) {
                // The array of a reference type is itself a reference type (an Array<Any?>).
                arrayType = reference<Any?>()
            }
            return arrayType!!
        }

        override fun newArray(length: Int): Any = arrayOfNulls<Any?>(length)

        override fun getArrayElement(array: Any?, index: Int): T = (array as kotlin.Array<T>)[index]

        override fun setArrayElement(array: Any?, index: Int, value: T) {
            (array as kotlin.Array<T>)[index] = value
        }

        override fun getArrayLength(array: Any?): Int = (array as kotlin.Array<*>).size

        override fun cloneArray(array: Any?): Any = (array as kotlin.Array<Any?>).copyOf()

        override fun fillArray(array: Any?, value: T) {
            (array as kotlin.Array<T>).fill(value)
        }

        override fun copyArrayTo(src: Any?, srcPos: Int, dest: Any?, destPos: Int, length: Int) {
            (src as kotlin.Array<Any?>).copyInto(dest as kotlin.Array<Any?>, destPos, srcPos, srcPos + length)
        }

        override fun arrayShallowEquals(array1: Any?, array2: Any?): Boolean =
            (array1 as kotlin.Array<*>).contentEquals(array2 as kotlin.Array<*>)
    }

    private class ByteType(private val d: Byte) : TypeDescriptor<Byte>() {
        override fun defaultValue(): Byte = d
        override fun isInstance(obj: Any?): Boolean = obj is Byte
        override fun arrayType(): TypeDescriptor<*> = BYTE_ARRAY
        override fun newArray(length: Int): Any = ByteArray(length)
        override fun getArrayElement(array: Any?, index: Int): Byte = (array as ByteArray)[index]
        override fun setArrayElement(array: Any?, index: Int, value: Byte) { (array as ByteArray)[index] = value }
        override fun getArrayLength(array: Any?): Int = (array as ByteArray).size
        override fun cloneArray(array: Any?): Any = (array as ByteArray).copyOf()
        override fun fillArray(array: Any?, value: Byte) { (array as ByteArray).fill(value) }
        override fun copyArrayTo(src: Any?, srcPos: Int, dest: Any?, destPos: Int, length: Int) {
            (src as ByteArray).copyInto(dest as ByteArray, destPos, srcPos, srcPos + length)
        }
        override fun arrayShallowEquals(array1: Any?, array2: Any?): Boolean =
            (array1 as ByteArray).contentEquals(array2 as ByteArray)
    }

    private class ShortType(private val d: Short) : TypeDescriptor<Short>() {
        override fun defaultValue(): Short = d
        override fun isInstance(obj: Any?): Boolean = obj is Short
        override fun arrayType(): TypeDescriptor<*> = SHORT_ARRAY
        override fun newArray(length: Int): Any = ShortArray(length)
        override fun getArrayElement(array: Any?, index: Int): Short = (array as ShortArray)[index]
        override fun setArrayElement(array: Any?, index: Int, value: Short) { (array as ShortArray)[index] = value }
        override fun getArrayLength(array: Any?): Int = (array as ShortArray).size
        override fun cloneArray(array: Any?): Any = (array as ShortArray).copyOf()
        override fun fillArray(array: Any?, value: Short) { (array as ShortArray).fill(value) }
        override fun copyArrayTo(src: Any?, srcPos: Int, dest: Any?, destPos: Int, length: Int) {
            (src as ShortArray).copyInto(dest as ShortArray, destPos, srcPos, srcPos + length)
        }
        override fun arrayShallowEquals(array1: Any?, array2: Any?): Boolean =
            (array1 as ShortArray).contentEquals(array2 as ShortArray)
    }

    private class IntType(private val d: Int) : TypeDescriptor<Int>() {
        override fun defaultValue(): Int = d
        override fun isInstance(obj: Any?): Boolean = obj is Int
        override fun arrayType(): TypeDescriptor<*> = INT_ARRAY
        override fun newArray(length: Int): Any = IntArray(length)
        override fun getArrayElement(array: Any?, index: Int): Int = (array as IntArray)[index]
        override fun setArrayElement(array: Any?, index: Int, value: Int) { (array as IntArray)[index] = value }
        override fun getArrayLength(array: Any?): Int = (array as IntArray).size
        override fun cloneArray(array: Any?): Any = (array as IntArray).copyOf()
        override fun fillArray(array: Any?, value: Int) { (array as IntArray).fill(value) }
        override fun copyArrayTo(src: Any?, srcPos: Int, dest: Any?, destPos: Int, length: Int) {
            (src as IntArray).copyInto(dest as IntArray, destPos, srcPos, srcPos + length)
        }
        override fun arrayShallowEquals(array1: Any?, array2: Any?): Boolean =
            (array1 as IntArray).contentEquals(array2 as IntArray)
    }

    private class LongType(private val d: Long) : TypeDescriptor<Long>() {
        override fun defaultValue(): Long = d
        override fun isInstance(obj: Any?): Boolean = obj is Long
        override fun arrayType(): TypeDescriptor<*> = LONG_ARRAY
        override fun newArray(length: Int): Any = LongArray(length)
        override fun getArrayElement(array: Any?, index: Int): Long = (array as LongArray)[index]
        override fun setArrayElement(array: Any?, index: Int, value: Long) { (array as LongArray)[index] = value }
        override fun getArrayLength(array: Any?): Int = (array as LongArray).size
        override fun cloneArray(array: Any?): Any = (array as LongArray).copyOf()
        override fun fillArray(array: Any?, value: Long) { (array as LongArray).fill(value) }
        override fun copyArrayTo(src: Any?, srcPos: Int, dest: Any?, destPos: Int, length: Int) {
            (src as LongArray).copyInto(dest as LongArray, destPos, srcPos, srcPos + length)
        }
        override fun arrayShallowEquals(array1: Any?, array2: Any?): Boolean =
            (array1 as LongArray).contentEquals(array2 as LongArray)
    }

    private class BooleanType(private val d: Boolean) : TypeDescriptor<Boolean>() {
        override fun defaultValue(): Boolean = d
        override fun isInstance(obj: Any?): Boolean = obj is Boolean
        override fun arrayType(): TypeDescriptor<*> = BOOLEAN_ARRAY
        override fun newArray(length: Int): Any = BooleanArray(length)
        override fun getArrayElement(array: Any?, index: Int): Boolean = (array as BooleanArray)[index]
        override fun setArrayElement(array: Any?, index: Int, value: Boolean) { (array as BooleanArray)[index] = value }
        override fun getArrayLength(array: Any?): Int = (array as BooleanArray).size
        override fun cloneArray(array: Any?): Any = (array as BooleanArray).copyOf()
        override fun fillArray(array: Any?, value: Boolean) { (array as BooleanArray).fill(value) }
        override fun copyArrayTo(src: Any?, srcPos: Int, dest: Any?, destPos: Int, length: Int) {
            (src as BooleanArray).copyInto(dest as BooleanArray, destPos, srcPos, srcPos + length)
        }
        override fun arrayShallowEquals(array1: Any?, array2: Any?): Boolean =
            (array1 as BooleanArray).contentEquals(array2 as BooleanArray)
    }

    private class CharType(private val d: Char) : TypeDescriptor<Char>() {
        override fun defaultValue(): Char = d
        override fun isInstance(obj: Any?): Boolean = obj is Char
        override fun arrayType(): TypeDescriptor<*> = CHAR_ARRAY
        override fun newArray(length: Int): Any = CharArray(length)
        override fun getArrayElement(array: Any?, index: Int): Char = (array as CharArray)[index]
        override fun setArrayElement(array: Any?, index: Int, value: Char) { (array as CharArray)[index] = value }
        override fun getArrayLength(array: Any?): Int = (array as CharArray).size
        override fun cloneArray(array: Any?): Any = (array as CharArray).copyOf()
        override fun fillArray(array: Any?, value: Char) { (array as CharArray).fill(value) }
        override fun copyArrayTo(src: Any?, srcPos: Int, dest: Any?, destPos: Int, length: Int) {
            (src as CharArray).copyInto(dest as CharArray, destPos, srcPos, srcPos + length)
        }
        override fun arrayShallowEquals(array1: Any?, array2: Any?): Boolean =
            (array1 as CharArray).contentEquals(array2 as CharArray)
    }

    // In --unicode-char mode a Dafny char is a CodePoint boxing an Int; the backing array
    // is an IntArray of code points.
    private class UnicodeCharType(private val d: CodePoint) : TypeDescriptor<CodePoint>() {
        override fun defaultValue(): CodePoint = d
        override fun isInstance(obj: Any?): Boolean = obj is CodePoint
        override fun arrayType(): TypeDescriptor<*> = UNICODE_CHAR_ARRAY
        override fun newArray(length: Int): Any = IntArray(length)
        override fun getArrayElement(array: Any?, index: Int): CodePoint = CodePoint.valueOf((array as IntArray)[index])
        override fun setArrayElement(array: Any?, index: Int, value: CodePoint) { (array as IntArray)[index] = value.value() }
        override fun getArrayLength(array: Any?): Int = (array as IntArray).size
        override fun cloneArray(array: Any?): Any = (array as IntArray).copyOf()
        override fun fillArray(array: Any?, value: CodePoint) { (array as IntArray).fill(value.value()) }
        override fun copyArrayTo(src: Any?, srcPos: Int, dest: Any?, destPos: Int, length: Int) {
            (src as IntArray).copyInto(dest as IntArray, destPos, srcPos, srcPos + length)
        }
        override fun arrayShallowEquals(array1: Any?, array2: Any?): Boolean =
            (array1 as IntArray).contentEquals(array2 as IntArray)
    }

    companion object {
        // Reference descriptor with no default and an always-true instance check. Suitable
        // for boxed reference element types where the default value is null and precise
        // instance checks aren't needed (Dafny's type system guarantees soundness).
        fun <T> reference(): TypeDescriptor<T> =
            ReferenceType(Initializer { null as T? }, { true })

        fun <T> reference(instanceCheck: (Any?) -> Boolean): TypeDescriptor<T> =
            ReferenceType(Initializer { null as T? }, instanceCheck)

        fun <T> referenceWithDefault(defaultValue: T?): TypeDescriptor<T> =
            ReferenceType(Initializer { defaultValue }, { true })

        fun <T> referenceWithDefault(defaultValue: T?, instanceCheck: (Any?) -> Boolean): TypeDescriptor<T> =
            ReferenceType(Initializer { defaultValue }, instanceCheck)

        fun <T> referenceWithInitializer(initializer: Initializer<T>): TypeDescriptor<T> =
            ReferenceType(initializer, { true })

        fun <T> referenceWithInitializer(instanceCheck: (Any?) -> Boolean, initializer: Initializer<T>): TypeDescriptor<T> =
            ReferenceType(initializer, instanceCheck)

        // The array of a reference type is itself a reference type; the source descriptor
        // only contributes its (irrelevant here) identity, so this just makes a fresh
        // reference descriptor with the given initializer.
        fun <T> referenceWithInitializerAndTypeDescriptor(
            typeDescriptor: TypeDescriptor<*>,
            initializer: Initializer<T>
        ): TypeDescriptor<T> = ReferenceType(initializer, { true })

        fun byteWithDefault(d: Byte): TypeDescriptor<Byte> = ByteType(d)
        fun shortWithDefault(d: Short): TypeDescriptor<Short> = ShortType(d)
        fun intWithDefault(d: Int): TypeDescriptor<Int> = IntType(d)
        fun longWithDefault(d: Long): TypeDescriptor<Long> = LongType(d)
        fun booleanWithDefault(d: Boolean): TypeDescriptor<Boolean> = BooleanType(d)
        fun charWithDefault(d: Char): TypeDescriptor<Char> = CharType(d)
        fun unicodeCharWithDefault(d: Int): TypeDescriptor<CodePoint> = UnicodeCharType(CodePoint.valueOf(d))

        val BYTE: TypeDescriptor<Byte> = ByteType(0.toByte())
        val SHORT: TypeDescriptor<Short> = ShortType(0.toShort())
        val INT: TypeDescriptor<Int> = IntType(0)
        val LONG: TypeDescriptor<Long> = LongType(0L)
        val BOOLEAN: TypeDescriptor<Boolean> = BooleanType(false)
        val CHAR: TypeDescriptor<Char> = CharType('D') // CharType.DefaultValue in the Dafny source
        val UNICODE_CHAR: TypeDescriptor<CodePoint> = UnicodeCharType(CodePoint.valueOf('D'.code))

        val BIG_INTEGER: TypeDescriptor<BigInteger> = referenceWithDefault(BigInteger.ZERO) { it is BigInteger }
        val BIG_RATIONAL: TypeDescriptor<BigRational> = referenceWithDefault(BigRational.ZERO) { it is BigRational }

        // Dafny `object` is a nullable reference type, so its descriptor is over Any?.
        val OBJECT: TypeDescriptor<Any?> = reference()

        val BYTE_ARRAY: TypeDescriptor<ByteArray> = reference { it is ByteArray }
        val SHORT_ARRAY: TypeDescriptor<ShortArray> = reference { it is ShortArray }
        val INT_ARRAY: TypeDescriptor<IntArray> = reference { it is IntArray }
        val LONG_ARRAY: TypeDescriptor<LongArray> = reference { it is LongArray }
        val BOOLEAN_ARRAY: TypeDescriptor<BooleanArray> = reference { it is BooleanArray }
        val CHAR_ARRAY: TypeDescriptor<CharArray> = reference { it is CharArray }
        val UNICODE_CHAR_ARRAY: TypeDescriptor<IntArray> = reference { it is IntArray }

        // Arrow types are erased to a single reference descriptor (the generated code
        // supplies the precise function type at the call site).
        fun <A, R, T> function(argType: TypeDescriptor<A>, returnType: TypeDescriptor<R>): TypeDescriptor<T> =
            reference()
    }
}
