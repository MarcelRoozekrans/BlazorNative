package io.blazornative.shell

import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * #296's counter-argument, measured instead of argued.
 *
 * The issue deferred the Kotlin fix on a claim about the PLATFORM: Android's
 * intent-filter scheme matching is case-sensitive, so `BLAZORNATIVE://…` never
 * reaches MainActivity and parseDeepLinkRoute never sees it. That claim decides
 * whether the fix is user-visible or parity hygiene, so it is pinned here.
 *
 * Two facts, because they answer different questions:
 *  - IMPLICIT: does the manifest filter resolve an uppercase scheme? This is the
 *    claim. Its positive control is the lowercase scheme in the same test, which
 *    must resolve, so an empty result means the case, not a broken query.
 *  - EXPLICIT: an intent naming the component RESOLVES to MainActivity independent
 *    of the filter and the scheme — resolution is not delivery, only proof that the
 *    parser CAN be handed an uppercase scheme this way, whatever the first fact says
 *    about the filter. The contrast row inside that test (an unrelated https scheme,
 *    also resolved) is what makes "independent of the scheme" a measured claim rather
 *    than an inference from a single case.
 *
 * LIMITS: measured on the android-instrumented AVD image only. A different API
 * level could resolve differently, which is exactly why this is a pin and not a
 * sentence. It does not cover browsers, which may lowercase a URL's scheme before
 * firing the intent.
 */
@RunWith(AndroidJUnit4::class)
class BnDeepLinkReachabilityTest {

    private val context = InstrumentationRegistry.getInstrumentation().targetContext
    private val mainActivity = MainActivity::class.java.name

    private fun resolved(intent: Intent): List<String> =
        context.packageManager
            .queryIntentActivities(intent, PackageManager.MATCH_DEFAULT_ONLY)
            .map { it.activityInfo.name }

    private fun view(url: String) =
        Intent(Intent.ACTION_VIEW, Uri.parse(url))
            .addCategory(Intent.CATEGORY_BROWSABLE)
            .setPackage(context.packageName)

    @Test
    fun theIntentFilter_resolvesLowercase_butNotAnUppercaseScheme() {
        assertTrue("positive control: blazornative:// must resolve to MainActivity",
            resolved(view("blazornative://settings")).contains(mainActivity))
        assertEquals("BLAZORNATIVE:// resolution (measured; see the class KDoc)",
            emptyList<String>(), resolved(view("BLAZORNATIVE://settings")))
    }

    @Test
    fun anExplicitIntent_resolvesToMainActivity_independentOfTheScheme() {
        val uppercase = Intent(Intent.ACTION_VIEW, Uri.parse("BLAZORNATIVE://settings"))
            .setClassName(context.packageName, mainActivity)
        assertEquals(listOf(mainActivity), resolved(uppercase))

        // Contrast, in the same fact: queryIntentActivities on a component-named intent
        // skips filter matching (and MATCH_DEFAULT_ONLY) entirely, so it resolves for ANY
        // URI, not because BLAZORNATIVE's case was accepted. An unrelated scheme resolving
        // identically is what makes "independent of the scheme" measured rather than implied
        // by the single uppercase case above.
        val unrelatedScheme = Intent(Intent.ACTION_VIEW, Uri.parse("https://example.com/settings"))
            .setClassName(context.packageName, mainActivity)
        assertEquals(listOf(mainActivity), resolved(unrelatedScheme))
    }
}
