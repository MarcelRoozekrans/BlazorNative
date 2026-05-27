package io.blazornative.jni

import kotlinx.serialization.json.Json

/**
 * Phase 2.4 host-side parser. Splits captured stdout into lines, picks the
 * ones prefixed with "[FRAME] ", deserializes the JSON tail to a RenderFrame.
 *
 * Lines without the prefix are silently ignored — stdout interleaves [BOOT]
 * markers, [FRAME] lines, and (in future) [LOG] / [ERR] channels. The parser
 * is a routing-by-prefix step; structural validation happens via
 * kotlinx.serialization's strict decode.
 *
 * Wire contract: src/BlazorNative.Renderer/PatchProtocol.cs.
 */
object FrameStreamParser {
    private const val PREFIX = "[FRAME] "

    private val json = Json {
        ignoreUnknownKeys = true
        classDiscriminator = "op"
    }

    fun parse(stdout: String): List<RenderFrame> =
        stdout.lineSequence()
            .filter { it.startsWith(PREFIX) }
            .map { json.decodeFromString<RenderFrame>(it.removePrefix(PREFIX)) }
            .toList()
}
