// GENERATED FILE — DO NOT EDIT (#278).
//
// Source of truth: src/deeplink-vectors.json
// Regenerate:      dotnet run --project tools/BlazorNative.WireGen
//
// One URL → route case, written ONCE in the manifest and asserted by all
// three suites. You cannot generate a deep-link parser; you CAN generate the
// table it must satisfy, which is the whole point — the two hand-written
// parsers disagreed for months because nothing compared them. Editing this
// file by hand puts one suite's expectations out of step with the others';
// WireVocabularyCodegenTests re-runs the emitter and byte-compares, so the
// edit fails the required build-test lane rather than reaching a device.

package io.blazornative.shell

/**
 * The deep-link URL → route cases, generated from the manifest. The DATA only —
 * the shell keeps its own parser and this suite its own assertion loop.
 */
object BnDeepLinkVectors {
    /** Every case. The second element is null when the URL must produce NO route. */
    val ALL: List<Pair<String, String?>> = listOf(
        // the common case — both shells already agree
        Pair("blazornative://settings", "/settings"),
        // THE DIVERGENCE (#278): Android returned /settings and dropped /audio
        Pair("blazornative://settings/audio", "/settings/audio"),
        // more than one extra segment
        Pair("blazornative://settings/audio/input", "/settings/audio/input"),
        // trailing slash trimmed — two spellings of one route would be a silent table miss
        Pair("blazornative://about/", "/about"),
        // degenerate: no host, no path
        Pair("blazornative://", "/"),
        // empty authority, explicit path — Android reads data.host (null here), iOS reads url.host (empty); the two APIs disagree about where the first segment lives
        Pair("blazornative:///settings", "/settings"),
        // wrong scheme is REJECTED, never coerced into a route
        Pair("https://example.com/settings", null),
    )
}
