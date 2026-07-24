// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

fun interface Function6<T0, T1, T2, T3, T4, T5, TResult> {
    fun apply(t0: T0, t1: T1, t2: T2, t3: T3, t4: T4, t5: T5): TResult

    companion object {
        fun <T0, T1, T2, T3, T4, T5, TResult> _typeDescriptor(t0: TypeDescriptor<T0>, t1: TypeDescriptor<T1>, t2: TypeDescriptor<T2>, t3: TypeDescriptor<T3>, t4: TypeDescriptor<T4>, t5: TypeDescriptor<T5>, tr: TypeDescriptor<TResult>): TypeDescriptor<Function6<T0, T1, T2, T3, T4, T5, TResult>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Function6<T0, T1, T2, T3, T4, T5, TResult>>
    }
}
