// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny


object BigOrdinal {
    fun IsLimit(b: BigInteger): Boolean {
        return b == BigInteger.ZERO
    }

    fun IsSucc(b: BigInteger): Boolean {
        return b.compareTo(BigInteger.ZERO) > 0
    }

    fun Offset(b: BigInteger): BigInteger {
        return b
    }

    fun IsNat(b: BigInteger): Boolean {
        return true // at runtime every ORDINAL is a natural number
    }
}
