// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

fun interface Function8<T0, T1, T2, T3, T4, T5, T6, T7, TResult> {
    fun apply(t0: T0, t1: T1, t2: T2, t3: T3, t4: T4, t5: T5, t6: T6, t7: T7): TResult

    companion object {
        fun <T0, T1, T2, T3, T4, T5, T6, T7, TResult> _typeDescriptor(t0: TypeDescriptor<T0>, t1: TypeDescriptor<T1>, t2: TypeDescriptor<T2>, t3: TypeDescriptor<T3>, t4: TypeDescriptor<T4>, t5: TypeDescriptor<T5>, t6: TypeDescriptor<T6>, t7: TypeDescriptor<T7>, tr: TypeDescriptor<TResult>): TypeDescriptor<Function8<T0, T1, T2, T3, T4, T5, T6, T7, TResult>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Function8<T0, T1, T2, T3, T4, T5, T6, T7, TResult>>
    }
}
