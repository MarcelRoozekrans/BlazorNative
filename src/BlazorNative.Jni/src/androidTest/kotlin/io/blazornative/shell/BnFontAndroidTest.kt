package io.blazornative.shell

import android.graphics.Typeface
import androidx.core.content.res.ResourcesCompat
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Font parity Gate A (#126) — the Android LOAD GUARD, the twin of iOS
 * BnFontTests.
 *
 * Gate A ships Inter (OFL, static Regular) as an Android resource font
 * (res/font/inter_regular.ttf) so a TextView CAN resolve it — no text-rendering
 * change yet (Gate B sets the typeface on the leaves + the Yoga measure path).
 * The failure this guards is the same one iOS guards: a font that silently does
 * not resolve, leaving every later `setTypeface` a no-op that renders the
 * platform default and re-breaks the parity the feature tightens.
 *
 * INSTRUMENTED, not a JVM unit test, and that is not a preference: a res/font
 * resource is resolved through aapt-generated `R.font` + the packaged
 * resource table, which only exist on-device. ResourcesCompat.getFont is the
 * same API Gate B will use to obtain the typeface. This repo cannot run the
 * androidTest lane on Windows, so it is dispatch/CI-verified.
 */
@RunWith(AndroidJUnit4::class)
class BnFontAndroidTest {

    @Test
    fun interRegularResolvesFromResFont_andIsNotTheDefault() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext

        // Resolves the bundled ttf through R.font. null here means the resource
        // font did not load — a missing/misnamed file under res/font, or a ttf
        // aapt rejected.
        val inter = ResourcesCompat.getFont(context, R.font.inter_regular)
        assertNotNull(
            "ResourcesCompat.getFont(R.font.inter_regular) returned null — the bundled Inter did " +
                "not resolve. Check res/font/inter_regular.ttf exists and is a valid TrueType font.",
            inter,
        )

        // The teeth: a real bundled font is a distinct Typeface, NOT the platform
        // default. If resolution ever silently fell back, this is what reddens
        // instead of Gate B shipping the wrong metrics green.
        assertNotEquals(
            "Inter resolved to Typeface.DEFAULT — the resource font is not being applied",
            Typeface.DEFAULT,
            inter,
        )
        assertTrue(
            "the resolved Inter typeface reports as the default family",
            inter != Typeface.DEFAULT && inter != Typeface.DEFAULT_BOLD,
        )
    }
}
