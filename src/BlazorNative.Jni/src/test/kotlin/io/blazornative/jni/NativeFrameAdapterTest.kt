package io.blazornative.jni

import com.sun.jna.Memory
import com.sun.jna.Pointer
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
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

    // ── 2. Synthetic offset-level decode ─────────────────────────────────────

    @Test
    fun read_syntheticFrame_roundTrips() {
        // Hand-write a CreateNode + ReplaceText (+ one unknown kind that must
        // be skipped) at the documented offsets, then decode.
        val patchCount = 3
        val patches = Memory(NativeFrameAdapter.PATCH_SIZE * patchCount).apply { clear() }

        // patch[0]: CreateNode nodeId=1 parent=-1 nodeType=3 (button)
        patches.setInt(0 + NativeFrameAdapter.PATCH_KIND, 1)
        patches.setInt(0 + NativeFrameAdapter.PATCH_NODE_ID, 1)
        patches.setInt(0 + NativeFrameAdapter.PATCH_PARENT, -1)
        patches.setInt(0 + NativeFrameAdapter.PATCH_NODE_TYPE, 3)

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
                RenderPatch.CreateNode(nodeId = 1, nodeType = "button", parentId = null),
                RenderPatch.ReplaceText(nodeId = 2, text = "héllo→世界"),
            ),
            decoded.patches
        )
    }

    // ── 3. Golden: native dll path == typed expected shape ───────────────────

    /** The Phase 2.8 Hello shape — 12 patches + the CommitFrame terminator.
     * Typed expected list transcribed from the retired hello-frame.json
     * fixture (3.0e). Node IDs are first-appearance order (1..6); CommitFrame
     * is normalized to (0, 0) on both sides before comparing. */
    private val expectedHelloPatches = listOf<RenderPatch>(
        RenderPatch.CreateNode(nodeId = 1, nodeType = "view"),
        RenderPatch.SetStyle(nodeId = 1, property = "backgroundColor", value = "#FFEEAA"),
        RenderPatch.SetStyle(nodeId = 1, property = "padding", value = "16"),
        RenderPatch.CreateNode(nodeId = 2, nodeType = "view", parentId = 1),
        RenderPatch.SetStyle(nodeId = 2, property = "fontSize", value = "24"),
        RenderPatch.CreateNode(nodeId = 3, nodeType = "text", parentId = 2),
        RenderPatch.ReplaceText(nodeId = 3, text = "Hello, BlazorNative!"),
        RenderPatch.CreateNode(nodeId = 4, nodeType = "button", parentId = 1),
        RenderPatch.CreateNode(nodeId = 5, nodeType = "text", parentId = 4),
        RenderPatch.ReplaceText(nodeId = 5, text = "Tap"),
        RenderPatch.CreateNode(nodeId = 6, nodeType = "input", parentId = 1),
        RenderPatch.UpdateProp(nodeId = 6, name = "placeholder", value = "Type here..."),
        RenderPatch.CommitFrame(frameId = 0, timestampMs = 0L),
    )

    @Test
    fun golden_mountHello_viaNativeDll_matchesExpectedShape() {
        initNativeHost()

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

        assertEquals(
            expectedHelloPatches,
            normalize(nativeFrame!!.patches),
            "native-callback patch list must equal the typed golden patch list"
        )
    }

    @Test
    fun mount_unknownComponent_returns1() {
        initNativeHost()
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
     * structural assertion (types, parent linkage, texts, props, order). */
    private fun normalize(patches: List<RenderPatch>): List<RenderPatch> {
        val idMap = mutableMapOf<Int, Int>()
        fun canon(id: Int): Int = idMap.getOrPut(id) { idMap.size + 1 }
        return patches.map { p ->
            when (p) {
                is RenderPatch.CreateNode  -> p.copy(nodeId = canon(p.nodeId), parentId = p.parentId?.let(::canon))
                is RenderPatch.AppendChild -> p.copy(parentId = canon(p.parentId), childId = canon(p.childId))
                is RenderPatch.RemoveNode  -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.UpdateProp  -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.ReplaceText -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.SetStyle    -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.AttachEvent -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.DetachEvent -> p.copy(nodeId = canon(p.nodeId))
                is RenderPatch.CommitFrame -> RenderPatch.CommitFrame(0, 0L)
            }
        }
    }

    /** Same options pattern as BootSmokeNativeTest — init is idempotent enough
     * for repeated calls within one test JVM (verifies accessors only). */
    private fun initNativeHost() {
        val osBytes = nulTerminated("test-host")
        val osMem = Memory(osBytes.size.toLong()).apply { write(0, osBytes, 0, osBytes.size) }
        val noteBytes = nulTerminated("phase-3.0d-golden")
        val noteMem = Memory(noteBytes.size.toLong()).apply { write(0, noteBytes, 0, noteBytes.size) }

        val opts = BlazorNativeInitOptions.ByReference().apply {
            platformInfoOs = osMem
            platformInfoApiLevel = 0
            platformInfoNote = noteMem
        }
        val result = NativeBindings.INSTANCE.blazornative_init(opts)
        assertEquals(0, result.status, "blazornative_init failed: ${result.errorMessage?.getString(0, "UTF-8")}")
    }
}
