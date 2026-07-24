package io.blazornative.jni

import com.sun.jna.Memory
import com.sun.jna.Pointer
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 3.0d Gate 2 — NativeFrameAdapter tests.
 *
 * Three layers:
 *  1. offsets_match_documented_layout — the Kotlin half of the dual
 *     struct-layout drift-catcher (the .NET half is PatchProtocolNativeTests.cs
 *     pinning Marshal.OffsetOf on the same numbers).
 *  2. read_syntheticFrame_roundTrips — hand-written bytes at the documented
 *     offsets decode to the expected sealed-class values (no dll involved).
 *  3. golden_mountHello_viaNativeDll_matchesExpectedShape — the golden test:
 *     mount(HelloComponent) inside the NativeAOT dll fires the JNA frame
 *     callback, and the adapter's output equals the typed expected patch list
 *     transcribed from the retired hello-frame.json fixture (Phase 3.0e); the
 *     wire shape lock lives here now, twinned with the .NET side's
 *     tests/BlazorNative.Runtime.Tests/HelloGoldenTests.cs.
 */
class NativeFrameAdapterTest {

    // ── 1. Drift-catcher ─────────────────────────────────────────────────────

    @Test
    fun offsets_match_documented_layout() {
        // Mirror of PatchProtocolNative.cs — if one of these fails, the ABI
        // drifted; fix the layout (or intentionally update BOTH sides + the
        // .NET offset test in PatchProtocolNativeTests.cs).
        assertEquals(48L, NativeFrameAdapter.PATCH_SIZE)
        assertEquals(0L, NativeFrameAdapter.PATCH_KIND)
        assertEquals(4L, NativeFrameAdapter.PATCH_NODE_ID)
        assertEquals(8L, NativeFrameAdapter.PATCH_PARENT)
        assertEquals(12L, NativeFrameAdapter.PATCH_NODE_TYPE)
        assertEquals(16L, NativeFrameAdapter.PATCH_AUX)
        assertEquals(24L, NativeFrameAdapter.PATCH_TEXT)
        assertEquals(32L, NativeFrameAdapter.PATCH_PROP_NAME)
        assertEquals(40L, NativeFrameAdapter.PATCH_PROP_VALUE)

        assertEquals(24L, NativeFrameAdapter.FRAME_SIZE)
        assertEquals(0L, NativeFrameAdapter.FRAME_PATCHES)
        assertEquals(8L, NativeFrameAdapter.FRAME_PATCH_COUNT)
        assertEquals(12L, NativeFrameAdapter.FRAME_FRAME_ID)
        assertEquals(16L, NativeFrameAdapter.FRAME_TIMESTAMP_MS)
    }

    // ── 1b. The nodeTypes vocabulary pin (Phase 7.3) ─────────────────────────

    /**
     * The Kotlin twin of Swift's `BnDriftTests` literal pin. Gate 1 (Phase 7.3)
     * recorded the gap by name: this suite pinned byte OFFSETS and one synthetic
     * decode (3 → "button"), but nothing pinned the array's LENGTH or CONTENT —
     * so a shell that missed a vocabulary extension decoded every new create to
     * the "?" fallback, and only a device golden could see it. EXACT content,
     * EXACT order: index IS the wire id (checkbox = 8, switch = 9, slider = 10;
     * Phase 7.4 adds modal = 11 and activityindicator = 12 — the overlay and
     * the measured leaf, a wire-VOCABULARY extension, not an ABI change;
     * FrameEncoder.MapNodeType is the .NET mirror, BnFrameAdapter.swift the
     * Swift one — the three move together or this reddens).
     */
    @Test
    fun nodeTypes_vocabulary_is_pinned_content_and_length() {
        assertEquals(
            listOf(
                "?", "view", "text", "button", "input", "image", "scroll", "picker",
                "checkbox", "switch", "slider", "modal", "activityindicator",
            ),
            NativeFrameAdapter.nodeTypes.toList(),
            "the nodeTypes vocabulary drifted — FrameEncoder.MapNodeType (.NET), this array " +
                "and BnFrameAdapter.swift are THREE MIRRORS that move together"
        )
    }

    /**
     * Phase 7.4 — the two new wire ids DECODE, at the offset level (the
     * "3 → button" synthetic decode's twin for the vocabulary this gate adds).
     * The content pin above answers "is the array right?"; this answers "does
     * a CreateNode carrying 11 actually come out as a `modal` create?" — the
     * question the WidgetMapper's `when (p.nodeType)` depends on. Written
     * red-first: with the entries absent, 11 falls onto the index-guard "?"
     * fallback and this test names the exact failure a device would only show
     * as a TextView where an overlay should be.
     */
    @Test
    fun read_decodes_modal_wire_id_11() {
        assertEquals("modal", decodeSingleCreateNodeType(11))
    }

    /** The measured leaf's wire id — see [read_decodes_modal_wire_id_11]. */
    @Test
    fun read_decodes_activityindicator_wire_id_12() {
        assertEquals("activityindicator", decodeSingleCreateNodeType(12))
    }

    /** Hand-writes ONE CreateNode with [nodeTypeId] at the documented offsets
     * and returns the decoded nodeType string. */
    private fun decodeSingleCreateNodeType(nodeTypeId: Int): String {
        val patches = Memory(NativeFrameAdapter.PATCH_SIZE).apply { clear() }
        patches.setInt(NativeFrameAdapter.PATCH_KIND, 1)
        patches.setInt(NativeFrameAdapter.PATCH_NODE_ID, 1)
        patches.setInt(NativeFrameAdapter.PATCH_PARENT, -1)
        patches.setInt(NativeFrameAdapter.PATCH_NODE_TYPE, nodeTypeId)
        patches.setInt(NativeFrameAdapter.PATCH_AUX, -1)
        val frame = Memory(NativeFrameAdapter.FRAME_SIZE).apply { clear() }
        frame.setPointer(NativeFrameAdapter.FRAME_PATCHES, patches)
        frame.setInt(NativeFrameAdapter.FRAME_PATCH_COUNT, 1)
        val decoded = NativeFrameAdapter.read(frame)
        return (decoded.patches.single() as RenderPatch.CreateNode).nodeType
    }

    // ── 2. Synthetic offset-level decode ─────────────────────────────────────

    @Test
    fun read_syntheticFrame_roundTrips() {
        // Hand-write a CreateNode + ReplaceText + DetachEvent (plus one unknown
        // kind AND the reserved-dormant kind 2, both of which must be skipped)
        // at the documented offsets, then decode.
        val patchCount = 5
        val patches = Memory(NativeFrameAdapter.PATCH_SIZE * patchCount).apply { clear() }

        // patch[0]: CreateNode nodeId=1 parent=-1 nodeType=3 (button)
        // insertIndex=2 rides AuxInt (Phase 3.3) — a non-default value proves
        // the field is actually decoded, not defaulted.
        patches.setInt(0 + NativeFrameAdapter.PATCH_KIND, 1)
        patches.setInt(0 + NativeFrameAdapter.PATCH_NODE_ID, 1)
        patches.setInt(0 + NativeFrameAdapter.PATCH_PARENT, -1)
        patches.setInt(0 + NativeFrameAdapter.PATCH_NODE_TYPE, 3)
        patches.setInt(0 + NativeFrameAdapter.PATCH_AUX, 2)

        // patch[1]: ReplaceText nodeId=2 text="héllo→世界"
        val textBytes = "héllo→世界".toByteArray(Charsets.UTF_8) + 0
        val textMem = Memory(textBytes.size.toLong()).apply { write(0, textBytes, 0, textBytes.size) }
        val p1 = NativeFrameAdapter.PATCH_SIZE
        patches.setInt(p1 + NativeFrameAdapter.PATCH_KIND, 5)
        patches.setInt(p1 + NativeFrameAdapter.PATCH_NODE_ID, 2)
        patches.setPointer(p1 + NativeFrameAdapter.PATCH_TEXT, textMem)

        // patch[2]: unknown kind 99 → adapter logs + skips (forward compat)
        val p2 = NativeFrameAdapter.PATCH_SIZE * 2
        patches.setInt(p2 + NativeFrameAdapter.PATCH_KIND, 99)

        // patch[3]: DetachEvent nodeId=4 handlerId=17 (AuxInt) eventName="click"
        // (Text field — Phase 3.3 pins the changed decode arm)
        val detachNameBytes = "click".toByteArray(Charsets.UTF_8) + 0
        val detachNameMem = Memory(detachNameBytes.size.toLong())
            .apply { write(0, detachNameBytes, 0, detachNameBytes.size) }
        val p3 = NativeFrameAdapter.PATCH_SIZE * 3
        patches.setInt(p3 + NativeFrameAdapter.PATCH_KIND, 8)
        patches.setInt(p3 + NativeFrameAdapter.PATCH_NODE_ID, 4)
        patches.setInt(p3 + NativeFrameAdapter.PATCH_AUX, 17)
        patches.setPointer(p3 + NativeFrameAdapter.PATCH_TEXT, detachNameMem)

        // patch[4]: kind 2 (retired AppendChild) → reserved-dormant since
        // Phase 3.3; must take the SAME unknown-kind skip arm as 99 — this
        // asserts the claim, not just documents it.
        val p4 = NativeFrameAdapter.PATCH_SIZE * 4
        patches.setInt(p4 + NativeFrameAdapter.PATCH_KIND, 2)
        patches.setInt(p4 + NativeFrameAdapter.PATCH_NODE_ID, 5)

        val frame = Memory(NativeFrameAdapter.FRAME_SIZE).apply { clear() }
        frame.setPointer(NativeFrameAdapter.FRAME_PATCHES, patches)
        frame.setInt(NativeFrameAdapter.FRAME_PATCH_COUNT, patchCount)
        frame.setInt(NativeFrameAdapter.FRAME_FRAME_ID, 7)
        frame.setLong(NativeFrameAdapter.FRAME_TIMESTAMP_MS, 123456789L)

        val decoded = NativeFrameAdapter.read(frame)

        assertEquals(7, decoded.frameId)
        assertEquals(123456789L, decoded.timestampMs)
        assertEquals(
            listOf(
                RenderPatch.CreateNode(nodeId = 1, nodeType = "button", parentId = null, insertIndex = 2),
                RenderPatch.ReplaceText(nodeId = 2, text = "héllo→世界"),
                RenderPatch.DetachEvent(nodeId = 4, handlerId = 17, eventName = "click"),
            ),
            decoded.patches
        )
    }

    /**
     * Phase 10.2 (#125.5) — the NULL-patches guard, parity with Swift
     * BnFrameAdapter's `nullPatchesPointer`. A frame that claims a positive
     * patchCount but carries a NULL patches pointer is corrupt: JNA's
     * getPointer returns null for a 0 field, so the decode loop would NPE
     * generically on the first getInt. The guard fails loud with the count
     * instead — the diagnostic the Swift twin already gives, so the
     * dropped-frame handler can name the cause. patchCount 0 with a null
     * pointer is NOT corrupt (an empty frame) and is covered by the loop simply
     * never running — asserted here too.
     */
    @Test
    fun read_positivePatchCount_withNullPatchesPointer_throwsDescriptively() {
        val frame = Memory(NativeFrameAdapter.FRAME_SIZE).apply { clear() }
        // FRAME_PATCHES left 0 (cleared) → JNA getPointer returns null.
        frame.setInt(NativeFrameAdapter.FRAME_PATCH_COUNT, 3)
        val ex = assertThrows(IllegalArgumentException::class.java) {
            NativeFrameAdapter.read(frame)
        }
        assertTrue(
            ex.message!!.contains("patches pointer is NULL"),
            "expected the descriptive null-patches diagnostic, got: ${ex.message}"
        )
    }

    // ── 3. Golden: native dll path == typed expected shape ───────────────────

    /** The Phase 2.8 Hello shape, Phase 3.2 interactive — 13 patches + the
     * CommitFrame terminator. Typed expected list transcribed from the retired
     * hello-frame.json fixture (3.0e), hand-updated for Phase 3.2 (+AttachEvent
     * on the button, counter in the fontSize-24 text). Node IDs are
     * first-appearance order (1..6); CommitFrame is normalized to (0, 0) on
     * both sides before comparing. AttachEvent.handlerId is runtime-assigned
     * (Blazor's process-global handler table) — the 0 here is the normalized
     * sentinel, zeroed on BOTH sides by [normalize] (the Kotlin mirror of
     * HelloGoldenTests.cs's AnyHandlerId relaxation); every OTHER field stays
     * pinned. */
    private val expectedHelloPatches = listOf<RenderPatch>(
        // Phase 3.3: every CreateNode carries insertIndex = -1 (Hello's mount
        // is pure appends) — the ONLY delta from the 3.2 golden (DoD #10).
        RenderPatch.CreateNode(nodeId = 1, nodeType = "view", insertIndex = -1),
        RenderPatch.SetStyle(nodeId = 1, property = "backgroundColor", value = "#FFEEAA"),
        RenderPatch.SetStyle(nodeId = 1, property = "padding", value = "16"),
        RenderPatch.CreateNode(nodeId = 2, nodeType = "view", parentId = 1, insertIndex = -1),
        RenderPatch.SetStyle(nodeId = 2, property = "fontSize", value = "24"),
        RenderPatch.CreateNode(nodeId = 3, nodeType = "text", parentId = 2, insertIndex = -1),
        RenderPatch.ReplaceText(nodeId = 3, text = "Hello, BlazorNative! (taps: 0)"),
        RenderPatch.CreateNode(nodeId = 4, nodeType = "button", parentId = 1, insertIndex = -1),
        RenderPatch.AttachEvent(nodeId = 4, eventName = "click", handlerId = 0),
        RenderPatch.CreateNode(nodeId = 5, nodeType = "text", parentId = 4, insertIndex = -1),
        RenderPatch.ReplaceText(nodeId = 5, text = "Tap"),
        RenderPatch.CreateNode(nodeId = 6, nodeType = "input", parentId = 1, insertIndex = -1),
        RenderPatch.UpdateProp(nodeId = 6, name = "placeholder", value = "Type here..."),
        RenderPatch.CommitFrame(frameId = 0, timestampMs = 0L),
    )

    @Test
    fun golden_mountHello_viaNativeDll_matchesExpectedShape() {
        initRuntime()

        val captured = java.util.concurrent.atomic.AtomicReference<RenderFrame?>()
        // A throw inside a JNA callback is swallowed by JNA's default handler
        // (stderr + return-to-native) — capture it and rethrow in the assertion
        // phase so adapter regressions surface in the test report, not stderr.
        val callbackError = java.util.concurrent.atomic.AtomicReference<Throwable?>()
        // MUST stay strongly referenced for the native call's duration — JNA
        // callbacks are GC-eligible once unreachable (local val is enough here).
        val callback = object : NativeBindings.FrameCallback {
            override fun invoke(frame: Pointer) {
                try {
                    // Copy INSIDE the callback: the native memory is callback-scoped.
                    captured.set(NativeFrameAdapter.read(frame))
                } catch (t: Throwable) {
                    callbackError.set(t)
                }
            }
        }
        assertEquals(0, NativeBindings.INSTANCE.blazornative_register_frame_callback(callback))

        val mountStatus = NativeBindings.INSTANCE.blazornative_mount(nulTerminated("HelloComponent"))
        assertEquals(0, mountStatus, "blazornative_mount(HelloComponent) failed")

        callbackError.get()?.let { throw AssertionError("frame callback threw while decoding", it) }
        val nativeFrame = captured.get()
        assertNotNull(nativeFrame, "frame callback did not fire during mount")

        // Runtime-assigned handlerIds must at least be positive (the .NET
        // twin's HandlerId > 0 assertion) — normalize() then zeroes them.
        nativeFrame!!.patches.filterIsInstance<RenderPatch.AttachEvent>().forEach {
            assertTrue(it.handlerId > 0, "AttachEvent.handlerId must be a positive runtime-assigned id, got ${it.handlerId}")
        }

        // normalize() BOTH sides: node-ID renumbering is identity on the
        // expected list (already first-appearance order), and handlerId is
        // zeroed on both — the AnyHandlerId-sentinel comparison.
        assertEquals(
            normalize(expectedHelloPatches),
            normalize(nativeFrame.patches),
            "native-callback patch list must equal the typed golden patch list"
        )
    }

    @Test
    fun mount_unknownComponent_returns1() {
        initRuntime()
        assertEquals(1, NativeBindings.INSTANCE.blazornative_mount(nulTerminated("NoSuchComponent")))
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private fun nulTerminated(s: String): ByteArray = s.toByteArray(Charsets.UTF_8) + 0

    /** CommitFrame carries live frameId/timestamp on the native path — compare
     * it by type only (zeroed), matching HelloGoldenTests' normalization.
     *
     * Node IDs are renumbered by first appearance: the runtime's node-ID
     * counter is process-global and monotonic across mounts, so any earlier
     * test that mounts a component (e.g. BlazorNativeRuntimeTest, Phase 3.0d
     * Gate 3) shifts the raw IDs. The typed golden list's 1..6 is itself just
     * first-appearance order, so canonical renumbering preserves every
     * structural assertion (types, parent linkage, texts, props, order).
     *
     * Attach/DetachEvent handlerIds are zeroed (Phase 3.2): Blazor's
     * event-handler table is likewise a process-global counter, so the raw id
     * depends on how many handlers earlier tests registered. Applied to BOTH
     * sides of the comparison — the expected list carries handlerId = 0 as the
     * sentinel, mirroring the .NET twin's AnyHandlerId normalization
     * (HelloGoldenTests.cs); every OTHER field stays load-bearing. */
    private fun normalize(patches: List<RenderPatch>): List<RenderPatch> {
        val idMap = mutableMapOf<Int, Int>()
        fun canon(id: Int): Int = idMap.getOrPut(id) { idMap.size + 1 }
        return patches.map { p ->
            when (p) {
                is RenderPatch.CreateNode  -> p.copy(nodeId = canon(p.nodeId), parentId = p.parentId?.let(::canon))
                is RenderPatch.RemoveNode  -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.UpdateProp  -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.ReplaceText -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.SetStyle    -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.AttachEvent -> p.copy(nodeId = canon(p.nodeId), handlerId = 0)
                is RenderPatch.DetachEvent -> p.copy(nodeId = canon(p.nodeId), handlerId = 0)
                is RenderPatch.CommitFrame -> RenderPatch.CommitFrame(0, 0L)
            }
        }
    }

    /** Same options pattern as BootSmokeNativeTest — init is idempotent enough
     * for repeated calls within one test JVM (verifies accessors only). */
    private fun initRuntime() {
        val osBytes = nulTerminated("test-host")
        val osMem = Memory(osBytes.size.toLong()).apply { write(0, osBytes, 0, osBytes.size) }
        val noteBytes = nulTerminated("phase-3.0d-golden") // era the golden was pinned, not the current phase
        val noteMem = Memory(noteBytes.size.toLong()).apply { write(0, noteBytes, 0, noteBytes.size) }

        val opts = BlazorNativeInitOptions.ByReference().apply {
            platformInfoOs = osMem
            platformInfoApiLevel = 0
            platformInfoNote = noteMem
        }
        val result = NativeBindings.INSTANCE.blazornative_init(opts.size(), opts)
        assertEquals(0, result.status, "blazornative_init failed: ${result.errorMessage?.getString(0, "UTF-8")}")
    }
}
