// Generated runtime class for the Dafny Kotlin target. Do not edit by hand.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "unused")

package dafny

fun interface Function1<T0, TResult> {
    fun apply(t0: T0): TResult

    companion object {
        fun <T0, TResult> _typeDescriptor(t0: TypeDescriptor<T0>, tr: TypeDescriptor<TResult>): TypeDescriptor<Function1<T0, TResult>> =
            TypeDescriptor.reference<Any?>() as TypeDescriptor<Function1<T0, TResult>>
    }
}
