// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

fun interface Function4<T0, T1, T2, T3, TResult> {
    fun apply(t0: T0, t1: T1, t2: T2, t3: T3): TResult

    companion object {
        fun <T0, T1, T2, T3, TResult> _typeDescriptor(t0: TypeDescriptor<T0>, t1: TypeDescriptor<T1>, t2: TypeDescriptor<T2>, t3: TypeDescriptor<T3>, tr: TypeDescriptor<TResult>): TypeDescriptor<Function4<T0, T1, T2, T3, TResult>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Function4<T0, T1, T2, T3, TResult>>
    }
}
