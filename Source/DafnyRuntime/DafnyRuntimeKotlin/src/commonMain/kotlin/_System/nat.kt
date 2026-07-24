// Class nat — the built-in Dafny `nat` subset type (int with x >= 0).
// Ported from the Java runtime's generated _System/nat.java.
@file:Suppress("UNCHECKED_CAST", "DEPRECATION", "ClassName")

package _System

import dafny.BigInteger

class nat {
    companion object {
        fun _Is(_source: BigInteger): Boolean {
            val x = _source
            return x.signum() != -1
        }

        private val _TYPE: dafny.TypeDescriptor<BigInteger> =
            dafny.TypeDescriptor.referenceWithInitializer { BigInteger.ZERO }

        fun _typeDescriptor(): dafny.TypeDescriptor<BigInteger> = _TYPE
    }
}
