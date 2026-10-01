package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections

/**
 * Phase 16.3 (#9) — the slow-handler warning names the APP's handler in the published
 * NativeAOT dll, not the component that forwards it.
 *
 * The warning keys a dispatch by the method its handler delegate runs, read through
 * `Delegate.Method`. Under NativeAOT a method can lack reflection metadata, and the key
 * would then fall back to the tree owner: for a BnButton, `BnButton` itself, which
 * forwards every app's OnClick from one line. The .NET suite runs on CoreCLR, where the
 * method always resolves, so only the published dll can say which key a device gets.
 *
 * The sample's SlowHandlerProbe has three BnButtons whose OnClick handlers each sleep
 * 500 ms, five times the budget: two different methods, and a capturing lambda. The
 * lambda's method is compiler-generated, on a closure class nested in the probe, and
 * Blazor stores it as a boxed EventCallback, so it also proves the renderer's
 * EventCallback accessor in the published dll. It captures every slow-handler
 * line through a BnLog sink and echoes them in a BnText when "Report" re-renders it.
 * "Report" also puts the previous sink back, so the probe's sink does not outlive this
 * test in the shared JVM process.
 * Owner keys give three warnings, one per method. The fallback would give ONE, naming
 * BnButton.
 *
 * DOES NOT COVER: the ChildContent shape, the budget, the once-per-handler
 * rule and the cap, all pinned on .NET by SlowHandlerWarningTests; the Android and iOS
 * builds, whose NativeAOT metadata policy is the same compiler's but not measured here.
 * The session is the process-global one every JVM test shares, so this assumes no other
 * test has already warned about these two methods; none clicks this probe.
 */
class SlowHandlerProbeTest {

    @Test
    fun three_bnbuttons_with_different_slow_handlers_warn_three_times_naming_the_apps_methods() {
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) }, onError = { _, _ -> })
        runtime.start(componentName = "SlowHandlerProbe", platformOs = "test-host", bridge = NoopBridge)
        try {
            val mount = frames.first()
            val one = clickHandlerOn(mount, containerOfText(mount, "Slow one"))
            val two = clickHandlerOn(mount, containerOfText(mount, "Slow two"))
            val threeNode = containerOfText(mount, "Slow three")
            val report = clickHandlerOn(mount, containerOfText(mount, "Report"))
            // Anchor: the echo starts empty, so any line below came from these clicks.
            assertEquals("slow-warnings:0", latestEcho(frames), "the probe's echo did not start at zero")

            assertEquals(0, runtime.dispatchEventBlocking(one, "click"))
            assertEquals(0, runtime.dispatchEventBlocking(two, "click"))
            // The lambda is a new closure on every render, so its handler id is read from
            // the LATEST frame, after the two clicks above re-rendered the probe.
            val three = latestClickHandlerOn(frames, threeNode)
            assertTrue(three != clickHandlerOn(mount, threeNode),
                "Slow three kept its mount-time handler id across two re-renders, so it is no longer " +
                    "a capturing lambda and this test no longer proves that shape")
            assertEquals(0, runtime.dispatchEventBlocking(three, "click"))
            assertEquals(0, runtime.dispatchEventBlocking(report, "click"))

            val lines = latestEcho(frames).split("\n")
            val warnings = lines.drop(1)
            assertEquals("slow-warnings:${warnings.size}", lines[0], "the echo's count and lines disagree: $lines")
            assertEquals(3, warnings.size,
                "three BnButtons with different slow handlers gave ${warnings.size} warnings, not 3. One " +
                    "warning naming BnButton means Delegate.Method did not resolve in the NativeAOT " +
                    "dll and the key fell back to the tree owner. Got: $warnings")
            assertTrue(warnings.single { it.contains("SlowHandlerProbe.SlowOne") }.isNotEmpty())
            assertTrue(warnings.single { it.contains("SlowHandlerProbe.SlowTwo") }.isNotEmpty())
            // The lambda: a compiler-generated method on a closure nested in the probe.
            assertTrue(warnings.single { it.contains("SlowHandlerProbe+") && it.contains("<BuildRenderTree>") }
                .isNotEmpty(), "no warning named the capturing lambda's generated method: $warnings")
            assertTrue(warnings.none { it.contains("BnButton") }, "a warning named BnButton: $warnings")
        } finally {
            runtime.retire()
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private object NoopBridge : ShellBridgeHandlers {
        override fun navigate(route: String) {}
        override fun currentRoute(): String = "/"
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "SlowHandlerProbeTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
        override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
            BridgeHostCallCompleter.complete(requestId, HostCallStatus.ERROR, null)
        }
    }

    private fun latestEcho(frames: List<RenderFrame>): String {
        val snapshot = synchronized(frames) { frames.toList() }
        return checkNotNull(
            snapshot.flatMap { it.patches.filterIsInstance<RenderPatch.ReplaceText>() }
                .lastOrNull { it.text.startsWith("slow-warnings:") }
        ) { "no frame carried the probe's 'slow-warnings:' echo; has SlowHandlerProbe moved?" }.text
    }

    private fun latestClickHandlerOn(frames: List<RenderFrame>, nodeId: Int): Int {
        val snapshot = synchronized(frames) { frames.toList() }
        return checkNotNull(
            snapshot.flatMap { it.patches.filterIsInstance<RenderPatch.AttachEvent>() }
                .lastOrNull { it.nodeId == nodeId && it.eventName == "click" }
        ) { "node $nodeId never had a click handler attached" }.handlerId
    }

    private fun createOf(frame: RenderFrame, nodeId: Int): RenderPatch.CreateNode =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.nodeId == nodeId }
        ) { "expected exactly one CreateNode for node $nodeId; got ${frame.patches}" }

    private fun containerOfText(frame: RenderFrame, text: String): Int {
        val t = checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.ReplaceText>().singleOrNull { it.text == text }
        ) { "expected exactly one ReplaceText '$text'; got ${frame.patches}" }
        return checkNotNull(createOf(frame, t.nodeId).parentId) { "text '$text' node must have a parent" }
    }

    private fun clickHandlerOn(frame: RenderFrame, nodeId: Int): Int =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .singleOrNull { it.nodeId == nodeId && it.eventName == "click" }
        ) { "expected exactly one click AttachEvent on node $nodeId; got ${frame.patches}" }.handlerId
}
