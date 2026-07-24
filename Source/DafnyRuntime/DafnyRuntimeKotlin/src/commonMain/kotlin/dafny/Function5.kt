// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

fun interface Function5<T0, T1, T2, T3, T4, TResult> {
    fun apply(t0: T0, t1: T1, t2: T2, t3: T3, t4: T4): TResult

    companion object {
        fun <T0, T1, T2, T3, T4, TResult> _typeDescriptor(t0: TypeDescriptor<T0>, t1: TypeDescriptor<T1>, t2: TypeDescriptor<T2>, t3: TypeDescriptor<T3>, t4: TypeDescriptor<T4>, tr: TypeDescriptor<TResult>): TypeDescriptor<Function5<T0, T1, T2, T3, T4, TResult>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Function5<T0, T1, T2, T3, T4, TResult>>
    }
}
