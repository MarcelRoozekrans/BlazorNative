package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Test
import kotlin.reflect.KVisibility
import kotlin.reflect.full.declaredMemberFunctions

/**
 * Phase 16.2 Task 5 (#346) — `dispatchHostEventAndWait` becomes `internal` and
 * test-only, by owner decision. `src/dispatch-surface.json` records
 * `"visibility": "internal"` for it, and `DispatchSurfaceDriftTests`
 * (BlazorNative.Runtime.Tests) reads the Kotlin and Swift SOURCE for the
 * `internal` keyword and compares both against the manifest. This test is the
 * JVM half: it proves the compiled class itself, not just the source text,
 * carries `internal` visibility.
 *
 * WHY KOTLIN REFLECTION, NOT THE BYTECODE ACC_PUBLIC FLAG: measured with javap
 * against `BlazorNativeRuntime.class` (built_in_kotlinc output) — an `internal`
 * member function still compiles `public final`:
 *   `public final int dispatchHostEventAndWait$BlazorNative_Jni(BnHostEvent, String)`
 * Only the compiler's name mangling (the `$BlazorNative_Jni` suffix) and
 * Kotlin's own `@Metadata` carry the internal/public distinction — the
 * `ACC_PUBLIC` flag is identical either way, so a test built on that flag
 * alone would pass whether this method were internal or public: a vacuous
 * pin. `KVisibility`, read through `kotlin-reflect`, reads the `@Metadata`
 * directly and does not depend on the mangled suffix's exact spelling.
 *
 * DOES NOT COVER: Swift's visibility (BnSwift cannot run on this JVM suite —
 * see BnHostTests/BnDispatchLaneTests.swift and the iOS CI lane) or the
 * cross-shell/manifest comparison, which is DispatchSurfaceDriftTests' job.
 * This test only proves the JVM side of the claim.
 */
class DispatchHostEventAndWaitVisibilityTest {

    /**
     * THE POSITIVE CONTROL (pin standard Rule 3): proves this reflection-based
     * check can actually tell `internal` from `public` on this Kotlin/JVM setup,
     * rather than reporting the same visibility for everything (which would make
     * the fact below pass no matter what the source declares).
     * `dispatchEvent` has no visibility modifier — Kotlin's default, `public` —
     * and is a member of the SAME class, compiled by the SAME `compileDebugKotlin`
     * task, so it is the closest available control.
     */
    @Test
    fun theVisibilityCheck_reallyDistinguishesPublicFromInternal() {
        val publicMember = BlazorNativeRuntime::class.declaredMemberFunctions
            .firstOrNull { it.name == "dispatchEvent" }
        assertNotNull(publicMember,
            "BlazorNativeRuntime declares no dispatchEvent member -- the control's own subject " +
                "moved, so this test cannot prove the check distinguishes anything.")
        assertEquals(KVisibility.PUBLIC, publicMember!!.visibility,
            "dispatchEvent carries no visibility modifier, which is Kotlin's default (public). " +
                "If this reads anything else, KVisibility itself is not reporting real " +
                "declared visibility on this Kotlin/JVM setup, and the fact below proves nothing.")
    }

    /** THE PIN. See the class doc for what this does and does not cover. */
    @Test
    fun dispatchHostEventAndWait_isInternal() {
        val members = BlazorNativeRuntime::class.declaredMemberFunctions
            .filter { it.name == "dispatchHostEventAndWait" }
        assertEquals(1, members.size,
            "BlazorNativeRuntime must declare exactly one dispatchHostEventAndWait member, found " +
                "${members.size}: ${members.map { it.visibility }}. 0 means it was renamed/removed " +
                "(update src/dispatch-surface.json and this test together) or declaredMemberFunctions " +
                "stopped seeing it. 2 or more means an overload was added: a public overload would " +
                "reopen the door #346 closed, and this pin cannot say which one it is looking at.")
        val member = members.single()
        assertEquals(KVisibility.INTERNAL, member.visibility,
            "dispatchHostEventAndWait must stay internal and test-only (16.2 Task 5, #346): no " +
                "production caller needs its rc any more -- both deep-link/notification navigate " +
                "and back moved to the fire-and-forget dispatchHostEvent. Making it public again " +
                "reopens the door #346 closed.")
    }
}
