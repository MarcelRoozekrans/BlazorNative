package io.blazornative.jni

import com.sun.jna.Pointer

class WasmtimeException(message: String, val source: String) : RuntimeException(message) {
    companion object {
        fun fromErrorPointer(source: String, errPtr: Pointer): WasmtimeException {
            val nameOut = WasmName()
            WasmtimeBindings.INSTANCE.wasmtime_error_message(errPtr, nameOut)
            val msg = nameOut.toString()
            WasmtimeBindings.INSTANCE.wasmtime_error_delete(errPtr)
            WasmtimeBindings.INSTANCE.wasm_byte_vec_delete(nameOut)
            return WasmtimeException("$source: $msg", source)
        }
    }
}
