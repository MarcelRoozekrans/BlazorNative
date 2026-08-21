package io.blazornative.shell

import android.content.Intent
import android.net.Uri
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

/**
 * The Android half of the deep-link parity harness (#278).
 *
 * Every case comes from `src/deeplink-vectors.json` via `BnDeepLinkVectors.g.kt`.
 * The Swift and .NET suites assert the SAME table — that is the point: the parse
 * is hand-written per shell, so the table it must satisfy is generated once.
 * Until Phase 13.4 the two parsers disagreed on every multi-segment route and
 * nothing compared them.
 *
 * ── WHY THIS IS AN INSTRUMENTED TEST AND NOT A JVM UNIT TEST ────────────────
 * The parser's whole job is to interpret `android.net.Uri`'s decomposition of a
 * URL, and `Uri.parse` is one of the android.jar stubs that THROWS
 * ("Method parse in android.net.Uri not mocked") on the host JVM. A unit-test
 * copy would have to hand-build the Uri, i.e. assert against a fake of the very
 * API under test. The vector table exists to catch a real-API disagreement
 * (Android's `data.host` is null for `blazornative:///settings` where iOS's
 * `url.host` is empty), so it has to run where the real API does.
 */
@RunWith(AndroidJUnit4::class)
class BnDeepLinkVectorTest {

    @Test
    fun everyVector_parsesToTheSharedExpectation() {
        for ((url, expected) in BnDeepLinkVectors.ALL) {
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse(url))
            assertEquals(
                "deep-link vector: $url",
                expected,
                MainActivity.parseDeepLinkRouteForTest(intent),
            )
        }
    }
}
