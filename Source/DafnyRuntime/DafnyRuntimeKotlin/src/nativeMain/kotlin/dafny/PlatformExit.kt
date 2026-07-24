// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny

// Native actual for dafnyExit.
actual fun dafnyExit(code: Int): Nothing = kotlin.system.exitProcess(code)
