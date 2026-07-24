// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

fun interface Function2<T0, T1, TResult> {
    fun apply(t0: T0, t1: T1): TResult

    companion object {
        fun <T0, T1, TResult> _typeDescriptor(t0: TypeDescriptor<T0>, t1: TypeDescriptor<T1>, tr: TypeDescriptor<TResult>): TypeDescriptor<Function2<T0, T1, TResult>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Function2<T0, T1, TResult>>
    }
}
