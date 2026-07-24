// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny

// JVM actual for dafnyExit: terminate the JVM.
actual fun dafnyExit(code: Int): Nothing = kotlin.system.exitProcess(code)
