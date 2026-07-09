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
 * Consumed by WidgetMapper to mutate the Android view tree per patch batch.
 */
data class RenderFrame(
    val frameId: Int,
    val timestampMs: Long,
    val patches: List<RenderPatch>
)

sealed class RenderPatch {
    data class CreateNode(val nodeId: Int, val nodeType: String, val parentId: Int? = null) : RenderPatch()

    data class UpdateProp(val nodeId: Int, val name: String, val value: String? = null) : RenderPatch()

    data class AppendChild(val parentId: Int, val childId: Int, val atIndex: Int = -1) : RenderPatch()

    data class RemoveNode(val nodeId: Int) : RenderPatch()

    data class ReplaceText(val nodeId: Int, val text: String) : RenderPatch()

    data class SetStyle(val nodeId: Int, val property: String, val value: String? = null) : RenderPatch()

    data class AttachEvent(val nodeId: Int, val eventName: String, val handlerId: Int) : RenderPatch()

    data class DetachEvent(val nodeId: Int, val handlerId: Int) : RenderPatch()

    data class CommitFrame(val frameId: Int, val timestampMs: Long) : RenderPatch()
}
