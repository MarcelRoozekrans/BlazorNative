package io.blazornative.jni

import com.sun.jna.Pointer

/**
 * Phase 3.0d: decodes a native `BlazorNativeFrame*` (handed to the registered
 * [NativeBindings.FrameCallback]) into the [RenderFrame] sealed-class model —
 * the same model the retired JSON/[FRAME]-line parser produced, so the
 * widget-mapping layer upstream stayed transport-agnostic across the switch.
 *
 * Layout contract — mirror of src/BlazorNative.Runtime/PatchProtocolNative.cs
 * (little-endian, 8-byte pointers). Reads use ONLY Pointer.getInt/getLong/
 * getPointer + getString(0, "UTF-8") at hardcoded offsets — deliberately no
 * JNA Structure reflection on the 60fps frame path. Offsets are pinned on
 * both sides: NativeFrameAdapterTest.kt here, PatchProtocolNativeTests.cs
 * (Marshal.OffsetOf) on the .NET side. If you change ANY field, update both.
 *
 * Field mapping — the exact inverse of FrameEncoder.cs's contractual table:
 *
 *   CreateNode  → CreateNode(nodeId, nodeTypes[nodeType], parentId = parent
 *                 unless -1, insertIndex = aux) — Phase 3.3 DoD #10: AuxInt
 *                 carries the host child insert index (-1 append, explicitly
 *                 encoded; 0 is a valid front index)
 *   (kind 2)    → reserved-dormant: AppendChild was DELETED in Phase 3.3
 *                 (CreateNode.insertIndex carries placement); the id is never
 *                 emitted and never reused, so it falls into the unknown-kind
 *                 log+skip arm below by design
 *   RemoveNode  → RemoveNode(nodeId)
 *   UpdateProp  → UpdateProp(nodeId, propName!!, propValue)
 *   ReplaceText → ReplaceText(nodeId, text!!)
 *   SetStyle    → SetStyle(nodeId, propName!!, propValue)
 *   AttachEvent → AttachEvent(nodeId, eventName = text!!, handlerId = aux)
 *   DetachEvent → DetachEvent(nodeId, handlerId = aux, eventName = text!!) —
 *                 Phase 3.3: eventName rides the same Text field AttachEvent
 *                 uses; handlerId is the ORIGINAL attach's id
 *   CommitFrame → CommitFrame(frameId = envelope frameId, timestampMs = envelope
 *                 timestampMs) — the envelope carries the truth; the patch's
 *                 NodeId duplicate of frameId is ignored.
 *   unknown     → logged + skipped (forward compat with newer runtimes).
 *
 * LIFETIME: everything the frame points at is arena memory owned by the
 * native side, valid ONLY for the duration of the callback — [read] must
 * complete (and copy all strings, which getString does) INSIDE the callback.
 *
 * EXCEPTION POSTURE: [read] throws on malformed input (NULL contractual
 * strings, out-of-range patch count). Inside a JNA callback that throw is
 * swallowed by JNA's default handler (stderr + return-to-native) and the
 * frame is silently dropped — see [NativeBindings.FrameCallback]. Gate 3's
 * BlazorNativeRuntime wraps the callback body in try/catch → android.util.Log
 * so the drop is deliberate and visible in logcat.
 */
object NativeFrameAdapter {

    // BlazorNativePatch — 48 bytes.
    const val PATCH_SIZE = 48L
    const val PATCH_KIND = 0L
    const val PATCH_NODE_ID = 4L       // CommitFrame: frameId
    const val PATCH_PARENT = 8L        // -1 = none
    const val PATCH_NODE_TYPE = 12L    // CreateNode only
    const val PATCH_AUX = 16L          // CreateNode: insertIndex (-1 = append); Attach/DetachEvent: handlerId
    // offset 20: Reserved0 padding — pointers below are 8-aligned.
    const val PATCH_TEXT = 24L         // ReplaceText: text; Attach/DetachEvent: eventName
    const val PATCH_PROP_NAME = 32L    // UpdateProp/SetStyle: name
    const val PATCH_PROP_VALUE = 40L   // UpdateProp/SetStyle: value; NULL = null

    // BlazorNativeFrame — 24 bytes.
    const val FRAME_SIZE = 24L
    const val FRAME_PATCHES = 0L       // BlazorNativePatch*
    const val FRAME_PATCH_COUNT = 8L
    const val FRAME_FRAME_ID = 12L
    const val FRAME_TIMESTAMP_MS = 16L

    /**
     * Index = BlazorNativeNodeType wire value (0 = None, never emitted for
     * CreateNode by the encoder).
     *
     * Phase 7.3: `checkbox = 8`, `switch = 9`, `slider = 10` — a wire-VOCABULARY
     * extension, not an ABI change (the id rides the existing int32 NodeType
     * field of the 48-byte patch). THREE MIRRORS move together:
     * `FrameEncoder.MapNodeType` (.NET — throws on unknown), this array, and
     * Swift's `BnFrameAdapter.nodeTypes` (both shells log-and-fallback to "?").
     *
     * Phase 7.4: `modal = 11` (the overlay — anchor + overlay shell-side, the
     * 6.2 synthetic-node machinery pointed at the root) and
     * `activityindicator = 12` (the measured leaf — ProgressBar /
     * UIActivityIndicatorView). Same extension shape, same three mirrors.
     *
     * `internal` and PINNED BY CONTENT — `NativeFrameAdapterTest.
     * nodeTypes_vocabulary_is_pinned_content_and_length`, the Kotlin twin of
     * Swift's `BnDriftTests` literal pin. Gate 1 recorded that NOTHING here
     * pinned length or content: a missed entry decoded every new create to "?"
     * and only a device golden could see it. The pin is what makes the next
     * vocabulary extension redden on the JVM lane instead.
     */
    internal val nodeTypes = arrayOf(
        "?", "view", "text", "button", "input", "image", "scroll", "picker",
        "checkbox", "switch", "slider", "modal", "activityindicator",
    )

    /** Sanity ceiling on patchCount: real frames are tens of patches; anything
     * beyond this means the frame pointer/layout is corrupted, and we'd rather
     * take the documented dropped-frame path (require → throw → JNA handler)
     * than chase garbage pointers at native speed. */
    const val MAX_PATCHES = 65_536

    fun read(framePtr: Pointer): RenderFrame {
        val patchesPtr = framePtr.getPointer(FRAME_PATCHES)
        val patchCount = framePtr.getInt(FRAME_PATCH_COUNT)
        require(patchCount in 0..MAX_PATCHES) {
            "corrupt BlazorNativeFrame: patchCount=$patchCount (allowed 0..$MAX_PATCHES)"
        }
        // Parity with Swift BnFrameAdapter's `nullPatchesPointer` guard
        // (BnFrameAdapter.swift): a positive patchCount with a NULL patches
        // pointer is a corrupt frame. JNA's getPointer returns null when the
        // field is 0, so without this the decode loop below NPEs generically on
        // the first getInt; fail loud with the count instead — the same
        // diagnostic the Swift twin already gives, and the twin's reason to
        // exist (a corrupt frame pointer must name itself, not surface as an
        // opaque NPE the dropped-frame handler cannot attribute).
        require(!(patchCount > 0 && patchesPtr == null)) {
            "corrupt BlazorNativeFrame: patchCount=$patchCount but the patches pointer is NULL"
        }
        val frameId = framePtr.getInt(FRAME_FRAME_ID)
        val timestampMs = framePtr.getLong(FRAME_TIMESTAMP_MS)

        val patches = ArrayList<RenderPatch>(patchCount)
        for (i in 0 until patchCount) {
            val base = i * PATCH_SIZE
            val kind = patchesPtr.getInt(base + PATCH_KIND)
            val nodeId = patchesPtr.getInt(base + PATCH_NODE_ID)
            val parent = patchesPtr.getInt(base + PATCH_PARENT)
            val aux = patchesPtr.getInt(base + PATCH_AUX)

            when (kind) {
                1 -> { // CreateNode
                    val nodeTypeIdx = patchesPtr.getInt(base + PATCH_NODE_TYPE)
                    patches.add(
                        RenderPatch.CreateNode(
                            nodeId = nodeId,
                            nodeType = nodeTypes.getOrElse(nodeTypeIdx) { "?" },
                            parentId = parent.takeIf { it != -1 },
                            insertIndex = aux,
                        )
                    )
                }
                // kind 2 (AppendChild) is reserved-dormant since Phase 3.3 —
                // never emitted; if it ever appears it takes the unknown-kind
                // log+skip arm below.
                3 -> patches.add(RenderPatch.RemoveNode(nodeId = nodeId))
                4 -> patches.add(
                    RenderPatch.UpdateProp(
                        nodeId = nodeId,
                        name = requireNotNull(readUtf8(patchesPtr, base + PATCH_PROP_NAME)) { "UpdateProp.propName NULL" },
                        value = readUtf8(patchesPtr, base + PATCH_PROP_VALUE),
                    )
                )
                5 -> patches.add(
                    RenderPatch.ReplaceText(
                        nodeId = nodeId,
                        text = requireNotNull(readUtf8(patchesPtr, base + PATCH_TEXT)) { "ReplaceText.text NULL" },
                    )
                )
                6 -> patches.add(
                    RenderPatch.SetStyle(
                        nodeId = nodeId,
                        property = requireNotNull(readUtf8(patchesPtr, base + PATCH_PROP_NAME)) { "SetStyle.propName NULL" },
                        value = readUtf8(patchesPtr, base + PATCH_PROP_VALUE),
                    )
                )
                7 -> patches.add(
                    RenderPatch.AttachEvent(
                        nodeId = nodeId,
                        eventName = requireNotNull(readUtf8(patchesPtr, base + PATCH_TEXT)) { "AttachEvent.eventName NULL" },
                        handlerId = aux,
                    )
                )
                8 -> patches.add(
                    RenderPatch.DetachEvent(
                        nodeId = nodeId,
                        handlerId = aux,
                        eventName = requireNotNull(readUtf8(patchesPtr, base + PATCH_TEXT)) { "DetachEvent.eventName NULL" },
                    )
                )
                9 -> patches.add(RenderPatch.CommitFrame(frameId = frameId, timestampMs = timestampMs))
                else ->
                    // Unknown wire id: a newer runtime is talking to an older
                    // shell. Skip (forward compat) but leave a trace.
                    System.err.println("[NativeFrameAdapter] skipping unknown patch kind $kind (patch $i of $patchCount)")
            }
        }

        return RenderFrame(frameId = frameId, timestampMs = timestampMs, patches = patches)
    }

    /** Reads a `const char*` field: NULL → null, else a copied UTF-8 String. */
    private fun readUtf8(patchesPtr: Pointer, fieldOffset: Long): String? =
        patchesPtr.getPointer(fieldOffset)?.getString(0, "UTF-8")
}
