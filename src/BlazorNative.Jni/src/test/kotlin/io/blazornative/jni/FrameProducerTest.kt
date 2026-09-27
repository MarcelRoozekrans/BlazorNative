package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections

/**
 * Phase 16.1 — the single-producer frame check ([BlazorNativeRuntime.checkFrameProducer]).
 *
 * Every frame of a session comes from .NET's render thread, and Android's WidgetMapper
 * buffers patches in an unsynchronized list until CommitFrame, which is only safe with
 * one producer. WidgetMapper lives in androidMain and cannot be built on the JVM, so the
 * check sits in the shared runtime's frame-callback wrapper, which every frame passes
 * through before it reaches onFrame (and so WidgetMapper.apply on Android).
 *
 * Three tests: the detector reports a second thread exactly once; it stays silent for one
 * thread; and the WIRING, through the published dll, reports when two sessions frame into
 * one runtime, so a wrapper that stopped calling the check reds here.
 *
 * The real-path negative (one session's frames, the mount and a re-render, report
 * nothing) is DispatchEventTest.async_dispatchEvent_rerenders_serially_on_one_non_lane_thread,
 * which asserts an empty onError.
 *
 * DOES NOT COVER: WidgetMapper itself (a producer that calls WidgetMapper.apply without
 * going through this runtime, e.g. a test harness, is not checked), or iOS.
 */
class FrameProducerTest {

    private fun runtimeWithErrors(): Pair<BlazorNativeRuntime, MutableList<String>> {
        val errors = Collections.synchronizedList(mutableListOf<String>())
        val runtime = BlazorNativeRuntime(onFrame = {}, onError = { msg, _ -> errors.add(msg) })
        return runtime to errors
    }

    private fun onThread(name: String, block: () -> Unit) {
        val t = Thread(block, name)
        t.start()
        t.join(5_000)
        check(!t.isAlive) { "thread '$name' did not finish" }
    }

    @Test
    fun frames_from_two_threads_report_once() {
        val (runtime, errors) = runtimeWithErrors()
        onThread("producer-one") { runtime.checkFrameProducer() }
        onThread("producer-two") { runtime.checkFrameProducer() }

        assertEquals(1, errors.size, "a second producer must be reported exactly once; got $errors")
        assertTrue(
            errors[0].contains("producer-two") && errors[0].contains("producer-one"),
            "the report must name both threads; got ${errors[0]}"
        )
    }

    @Test
    fun frames_from_one_thread_report_nothing() {
        val (runtime, errors) = runtimeWithErrors()
        onThread("producer-one") {
            repeat(3) { runtime.checkFrameProducer() }
        }
        assertEquals(emptyList<String>(), errors.toList(), "one producer must never be reported")
    }

    @Test
    fun a_second_session_framing_into_one_runtime_is_reported() {
        // Through the real frame-callback wrapper: session 1's render thread delivers the
        // first mount frame; after blazornative_shutdown the next mount builds a fresh
        // session with a NEW render thread, and its mount frame reaches the SAME runtime.
        val errors = Collections.synchronizedList(mutableListOf<String>())
        val frameThreads = Collections.synchronizedList(mutableListOf<Thread>())
        val runtime = BlazorNativeRuntime(
            onFrame = { frameThreads.add(Thread.currentThread()) },
            onError = { msg, _ -> errors.add(msg) },
        )
        runtime.start(platformOs = "test-host")
        val firstSessionFrames = frameThreads.size
        assertTrue(firstSessionFrames > 0, "the first mount must frame")
        assertEquals(emptyList<String>(), errors.toList(), "one session must not be reported")

        NativeBindings.INSTANCE.blazornative_shutdown()
        runtime.start(platformOs = "test-host")

        val threads = frameThreads.toList()
        println("[FrameProducerTest] frame threads across two sessions: ${threads.map { it.name }}")
        // Anchor: the two sessions really did frame on two different threads, so the check
        // had a second producer to see. Identity: both carry the render thread's name.
        assertEquals(
            2, threads.toSet().size,
            "expected two producer threads across the shutdown; got $threads"
        )
        assertTrue(
            errors.any { it.contains("second frame producer") },
            "frames from a second session's render thread reached this runtime unreported; " +
                "the frame-callback wrapper must call checkFrameProducer. errors=$errors"
        )
        runtime.retire()
    }
}
