package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

// ─────────────────────────────────────────────────────────────────────────────
// BnLogLevelWireTest — Phase 11.4 Gate B, design §5.4 and §8.1 pin 3.
//
// THE CLAIM THIS FILE PROVES: appending `logLevel` to BlazorNativeInitOptions
// COST ZERO BYTES. platformInfoKind sat at offset 24 followed by four bytes of
// TAIL PADDING (the struct's alignment is 8 because it holds pointers), so the
// new int lands in padding that was already allocated.
//
// AND THE PROOF IS THE PAIR, NOT EITHER HALF. `size == 32` alone would also hold
// if the field had never been added; `offsetOf == 28` alone would also hold if
// the struct had GROWN to 40. Together they say the field is there AND it was
// free — which is the whole reason the design chose this carrier over an export.
// ─────────────────────────────────────────────────────────────────────────────

class BnLogLevelWireTest {

    @Test
    fun `the init-options struct is still 32 bytes with logLevel at offset 28`() {
        val opts = BlazorNativeInitOptions.ByReference()

        assertEquals(32, opts.size(),
            "BlazorNativeInitOptions must stay 32 bytes — the .NET mirror asserts " +
                "Marshal.SizeOf == 32 and blazornative_init does NOT size-negotiate " +
                "(unlike register_bridge), so the two allocations must agree exactly.")
        // The offset is proven by MARSHALLING, not by asking JNA where it thinks
        // the field is: two distinguishable sentinels are written through the
        // Kotlin fields and read back out of the native memory BY OFFSET. That is
        // the same bytes blazornative_init dereferences, so this cannot pass while
        // the runtime reads something else.
        opts.platformInfoKind = 0x11111111
        opts.logLevel = 0x22222222
        opts.write()

        assertEquals(0x11111111, opts.pointer.getInt(24),
            "platformInfoKind must stay at offset 24 — Phase 10.0's field, and the C header, " +
                "the Swift call site and the .NET struct all pin it there.")
        assertEquals(0x22222222, opts.pointer.getInt(28),
            "logLevel must land in platformInfoKind's tail padding at 28 — that is what " +
                "makes it free. If it moved, the size above grew and the pair is broken.")
    }

    @Test
    fun `an unset logLevel is ordinal zero`() {
        // A shell that predates the field leaves the tail padding zero, and the
        // runtime resolves ordinal 0 to its own quiet default. Making UNSET the
        // Kotlin default is what keeps "say nothing" and "predate the field"
        // indistinguishable — which is why no migration is needed.
        assertEquals(0, BnLogLevel.UNSET)
        assertEquals(BnLogLevel.UNSET, BlazorNativeInitOptions.ByReference().logLevel)
    }

    @Test
    fun `the ordinals are the wire values BnLogLevel_cs declares`() {
        assertEquals(1, BnLogLevel.ERROR)
        assertEquals(2, BnLogLevel.WARN)
        assertEquals(3, BnLogLevel.INFO)
        assertEquals(4, BnLogLevel.DEBUG)
        assertEquals(5, BnLogLevel.VERBOSE)
    }

    @Test
    fun `fromName parses the app-facing knob case-insensitively`() {
        assertEquals(BnLogLevel.ERROR, BnLogLevel.fromName("Error"))
        assertEquals(BnLogLevel.WARN, BnLogLevel.fromName("warn"))
        assertEquals(BnLogLevel.WARN, BnLogLevel.fromName("WARNING"))
        assertEquals(BnLogLevel.INFO, BnLogLevel.fromName(" Info "))
        assertEquals(BnLogLevel.DEBUG, BnLogLevel.fromName("debug"))
        assertEquals(BnLogLevel.VERBOSE, BnLogLevel.fromName("Verbose"))
        assertEquals(BnLogLevel.VERBOSE, BnLogLevel.fromName("trace"))
    }

    @Test
    fun `an unrecognised name resolves to UNSET, never to silence`() {
        // A typo in an app's AndroidManifest must fall back to the runtime's
        // default (Warn), not turn logging off — the failure mode of "the log
        // config was wrong" must never be "there are no logs".
        assertEquals(BnLogLevel.UNSET, BnLogLevel.fromName("loud"))
        assertEquals(BnLogLevel.UNSET, BnLogLevel.fromName(""))
        assertEquals(BnLogLevel.UNSET, BnLogLevel.fromName(null))
    }
}
