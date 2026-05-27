package io.blazornative.jni

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonClassDiscriminator

/**
 * Phase 2.4 wire-format mirror of .NET's PatchProtocol.cs.
 *
 * Source of truth: src/BlazorNative.Renderer/PatchProtocol.cs. The .NET side
 * uses [JsonPolymorphic(TypeDiscriminatorPropertyName = "op")] with camelCase
 * naming — both contracts are mirrored here. If a new patch type lands in
 * PatchProtocol.cs, it must be added here too (and to FrameStreamParser's
 * sealed-class registry implicitly via kotlinx.serialization codegen).
 *
 * Used by FrameStreamParser to deserialize [FRAME] lines from the host's
 * captured stdout into typed Kotlin values, then handed to
 * MobileBridgeHandlers.onFrame for the host to act on (Phase 2.5+ widget
 * mapping plugs in here).
 */
@Serializable
data class RenderFrame(
    val frameId: Int,
    val timestampMs: Long,
    val patches: List<RenderPatch>
)

@OptIn(kotlinx.serialization.ExperimentalSerializationApi::class)
@Serializable
@JsonClassDiscriminator("op")
sealed class RenderPatch {
    @Serializable @SerialName("create")
    data class CreateNode(val nodeId: Int, val nodeType: String, val parentId: Int? = null) : RenderPatch()

    @Serializable @SerialName("prop")
    data class UpdateProp(val nodeId: Int, val name: String, val value: String? = null) : RenderPatch()

    @Serializable @SerialName("append")
    data class AppendChild(val parentId: Int, val childId: Int, val atIndex: Int = -1) : RenderPatch()

    @Serializable @SerialName("remove")
    data class RemoveNode(val nodeId: Int) : RenderPatch()

    @Serializable @SerialName("text")
    data class ReplaceText(val nodeId: Int, val text: String) : RenderPatch()

    @Serializable @SerialName("style")
    data class SetStyle(val nodeId: Int, val property: String, val value: String? = null) : RenderPatch()

    @Serializable @SerialName("event")
    data class AttachEvent(val nodeId: Int, val eventName: String, val handlerId: Int) : RenderPatch()

    @Serializable @SerialName("detach")
    data class DetachEvent(val nodeId: Int, val handlerId: Int) : RenderPatch()

    @Serializable @SerialName("commit")
    data class CommitFrame(val frameId: Int, val timestampMs: Long) : RenderPatch()
}
