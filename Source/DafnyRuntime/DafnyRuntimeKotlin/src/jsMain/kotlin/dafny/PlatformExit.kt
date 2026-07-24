// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT

package dafny

// JS actual for dafnyExit. On Node.js this calls process.exit; there is no portable
// process-exit in the browser, so as a fallback we throw to unwind.
external val process: dynamic

actual fun dafnyExit(code: Int): Nothing {
    val p = process
    if (p != null && p.exit != null) {
        p.exit(code)
    }
    throw RuntimeException("Program halted with exit code $code")
}
