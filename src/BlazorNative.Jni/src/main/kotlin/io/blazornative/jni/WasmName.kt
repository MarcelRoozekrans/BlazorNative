package io.blazornative.jni

import com.sun.jna.Pointer
import com.sun.jna.Structure

/**
 * Mirror of `wasm_byte_vec_t` from wasmtime.h:
 *     typedef struct wasm_byte_vec_t { size_t size; char* data; } wasm_byte_vec_t;
 *
 * wasm_name_t is an alias of wasm_byte_vec_t — used by wasmtime_error_message
 * to return UTF-8 error text.
 */
@Structure.FieldOrder("size", "data")
open class WasmName : Structure() {
    @JvmField var size: Long = 0
    @JvmField var data: Pointer? = null

    override fun toString(): String {
        val d = data ?: return "(null wasm_name)"
        if (size == 0L) return ""
        return String(d.getByteArray(0, size.toInt()), Charsets.UTF_8)
    }
}
