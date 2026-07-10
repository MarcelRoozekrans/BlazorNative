package io.blazornative.jni

/**
 * In-memory mirror of .NET's patch model (src/BlazorNative.Renderer/PatchProtocol.cs).
 *
 * The wire contract is the typed-struct C ABI decoded by [NativeFrameAdapter]
 * at documented byte offsets — NOT JSON (the kotlinx-serialization layer was
 * deleted with the WASM era in Phase 3.0e). If a new patch type lands in
 * PatchProtocol.cs, it must be added here AND to NativeFrameAdapter's kind
 * switch (plus the .NET FrameEncoder).
 *
 * Phase 3.3 mapping notes (DoD #8/#10, mirroring FrameEncoder.cs):
 *  - [RenderPatch.CreateNode.insertIndex] rides the wire's AuxInt: the host
 *    child index to insert at under [RenderPatch.CreateNode.parentId]; -1 =
 *    append (EXPLICITLY encoded — 0 is a valid front index). MOUNT frames can
 *    carry non-append values too (an interleaved child component's create).
 *  - [RenderPatch.DetachEvent.eventName] rides the wire's Text field (the
 *    same field AttachEvent uses). handlerId is the ORIGINAL attach's id.
 *    Re-attach for the same (node, event) REPLACES last-wins with NO
 *    preceding DetachEvent — hosts swap their watcher, never stack.
 *  - AppendChild DELETED: creation carries its own placement via insertIndex.
 *    Its wire kind (2) stays reserved-dormant in .NET's BlazorNativePatchKind
 *    only — never emitted, never reused; the adapter treats 2 as unknown.
 *
 * Consumed by WidgetMapper to mutate the Android view tree per patch batch.
 */
data class RenderFrame(
    val frameId: Int,
    val timestampMs: Long,
    val patches: List<RenderPatch>
)

sealed class RenderPatch {
    data class CreateNode(
        val nodeId: Int,
        val nodeType: String,
        val parentId: Int? = null,
        val insertIndex: Int = -1,
    ) : RenderPatch()

    data class UpdateProp(val nodeId: Int, val name: String, val value: String? = null) : RenderPatch()

    data class RemoveNode(val nodeId: Int) : RenderPatch()

    data class ReplaceText(val nodeId: Int, val text: String) : RenderPatch()

    data class SetStyle(val nodeId: Int, val property: String, val value: String? = null) : RenderPatch()

    data class AttachEvent(val nodeId: Int, val eventName: String, val handlerId: Int) : RenderPatch()

    data class DetachEvent(val nodeId: Int, val handlerId: Int, val eventName: String) : RenderPatch()

    data class CommitFrame(val frameId: Int, val timestampMs: Long) : RenderPatch()
}
