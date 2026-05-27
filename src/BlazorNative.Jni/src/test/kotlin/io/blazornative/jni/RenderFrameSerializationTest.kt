package io.blazornative.jni

import kotlinx.serialization.json.Json
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue

/**
 * Phase 2.4 wire-format guard. Locks the Kotlin sealed-class hierarchy to
 * the .NET PatchProtocol.cs shape — camelCase property names, "op"
 * discriminator. If .NET's JsonSerializerContext output changes shape, this
 * test fails BEFORE any wasmtime run, isolating wire-format regressions
 * from runtime regressions.
 */
class RenderFrameSerializationTest {

    private val json = Json { ignoreUnknownKeys = true; classDiscriminator = "op" }

    @Test
    fun deserializes_dotnet_emitted_frame_shape() {
        // Sample .wasm output shape — matches what RendererJsonContext serializes
        // for the sentinel component.
        val input = """
            {"frameId":7,"timestampMs":1748275200000,"patches":[
              {"op":"create","nodeId":1,"nodeType":"view"},
              {"op":"create","nodeId":2,"nodeType":"text"},
              {"op":"text","nodeId":2,"text":"frame-self-test"},
              {"op":"commit","frameId":7,"timestampMs":1748275200000}
            ]}
        """.trimIndent()

        val frame = json.decodeFromString<RenderFrame>(input)

        assertEquals(7, frame.frameId)
        assertEquals(4, frame.patches.size)
        assertTrue(frame.patches[0] is RenderPatch.CreateNode)
        assertTrue(frame.patches[3] is RenderPatch.CommitFrame)
        val text = frame.patches[2] as RenderPatch.ReplaceText
        assertEquals("frame-self-test", text.text)
    }
}
