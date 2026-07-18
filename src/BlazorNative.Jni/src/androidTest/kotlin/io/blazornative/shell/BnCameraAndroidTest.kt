package io.blazornative.shell

import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.media.ExifInterface
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.CameraStatus
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.io.FileOutputStream
import java.util.concurrent.atomic.AtomicReference

// ─────────────────────────────────────────────────────────────────────────────
// Phase 9.3 Gate 2 (M9 DoD #5) — camera photo capture on the AVD. The on-device third
// of BnCameraDemoTests.cs (.NET, DevHostBridge drives every status headless) and
// CameraTest.kt (JVM, through the dll). THIS proves AndroidShellBridge's REAL flow: the
// FileProvider output URI, the downscale + EXIF-normalization, the app-cache capture dir +
// the prune backstop, and — the phase headline — the capture → file → BnImage composition.
//
// THE PROVEN / UNPROVEN SPLIT (the design's honesty, and camera is where the emulator is
// LEAST like reality):
//   • PROVEN here, deterministically (the AVD's emulated back camera writes a synthetic
//     scene, but the real camera-app SHUTTER is not CI-drivable, so the capture RESULT is
//     seam-driven — CameraCapture writes synthetic JPEG bytes THROUGH the FileProvider URI,
//     exactly as the system camera app writes to EXTRA_OUTPUT, and delivers the result
//     through the SAME processing path):
//       – a capture writes a REAL JPEG to the app-cache dir WITH BYTES, via the FileProvider
//         (a mis-authoritied provider fails the write → the "has bytes" assertion reds);
//       – the returned file:// path becomes a BnImage Src that MEASURES at the demo's
//         definite 240×320 box and PAINTS the loaded file (Coil loads the local file) — THE
//         COMPOSITION (camera → file → BnImage);
//       – the downscale BOUNDS the long edge to maxDim;
//       – EXIF is NORMALIZED — a rotated input yields upright output pixels AND an identity
//         tag (Coil/Kingfisher, which honor EXIF on decode, do not rotate a second time);
//       – the prune backstop keeps only the last N captures;
//       – a Cancelled result is DATA within a bounded await (NO HANG), carrying no path;
//       – `check` reports availability as data.
//   • UNPROVEN until a physical phone (the design's split, named not smuggled): the REAL
//     system camera-app UI + a real sensor + a real photo of the real world + REAL EXIF
//     orientation off a real sensor (the emulator's synthetic frame is a known, un-rotated
//     scene — the normalization is exercised against a synthetic ROTATED input here, but not
//     against a genuinely rotated capture) + real full-resolution files.
// ─────────────────────────────────────────────────────────────────────────────

/** The keystore-test-style half — AndroidShellBridge's camera core driven DIRECTLY (no
 * Activity, no .NET), asserting the FileProvider write, the downscale, the EXIF-normalize and
 * the prune backstop against synthetic inputs. */
@RunWith(AndroidJUnit4::class)
class CameraCaptureAndroidTest {

    private lateinit var bridge: AndroidShellBridge

    @Before
    fun setup() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        bridge = AndroidShellBridge(ctx) { _, _ -> }
        bridge.clearCaptureDirForTest()
    }

    @After
    fun cleanup() {
        bridge.clearCaptureDirForTest()
    }

    // ── A capture writes a REAL JPEG through the FileProvider, with bytes ─────

    @Test
    fun capture_writes_a_real_jpeg_through_the_fileprovider_with_bytes() {
        // The bytes are written THROUGH the FileProvider content URI (as the camera app writes
        // to EXTRA_OUTPUT). MUTATION: mis-authority the <provider> → FileProvider.getUriForFile
        // throws → the capture never produces a file → this reds (no path, no bytes).
        val outcome = bridge.captureThroughProviderForTest(
            makeJpeg(800, 600), AndroidShellBridge.CaptureOptions(maxDim = 2048, quality = 85))

        assertEquals("a written capture is Captured", CameraStatus.CAPTURED, outcome.status)
        assertNotNull("Captured carries a file:// path", outcome.path)
        assertTrue("the path is a file:// URI, got ${outcome.path}", outcome.path!!.startsWith("file://"))

        val file = File(outcome.path!!.removePrefix("file://"))
        assertTrue("the capture file exists on disk", file.exists())
        assertTrue("the capture file has REAL BYTES (${file.length()})", file.length() > 0)
        assertEquals("the reported byte size is the file's own", file.length(), outcome.bytes)
        assertTrue("positive dimensions", outcome.width > 0 && outcome.height > 0)
    }

    // ── The downscale BOUNDS the long edge to maxDim ─────────────────────────

    @Test
    fun capture_downscales_to_maxdim_on_the_long_edge() {
        // A 4000×3000 scene captured at maxDim 1024 must land with its LONG edge ≤ 1024.
        // MUTATION: skip the downscale (return the full-res bitmap) → long edge 4000 > 1024 → reds.
        val maxDim = 1024
        val outcome = bridge.captureThroughProviderForTest(
            makeJpeg(4000, 3000), AndroidShellBridge.CaptureOptions(maxDim = maxDim, quality = 85))

        assertEquals(CameraStatus.CAPTURED, outcome.status)
        val longEdge = maxOf(outcome.width, outcome.height)
        assertTrue("the long edge ($longEdge) must be bounded to maxDim ($maxDim)", longEdge <= maxDim)
        // …and it did NOT collapse to nothing (a downscale that zeroed the image would also pass
        // "≤ maxDim"): the aspect ratio 4:3 is preserved and the image is comfortably sized.
        assertTrue("the downscale preserved a real image (${outcome.width}×${outcome.height})",
            outcome.width >= 512 && outcome.height >= 384)
    }

    // ── EXIF is NORMALIZED — rotation baked into pixels AND the tag reset ─────

    @Test
    fun capture_normalizes_exif_baking_rotation_and_resetting_the_tag() {
        // A LANDSCAPE 400×200 buffer tagged ORIENTATION_ROTATE_90 DISPLAYS as portrait 200×400.
        // The shell bakes that rotation into the pixels (upright output) and re-encodes — a
        // re-encoded JPEG carries no orientation tag, so a consumer that honors EXIF (Coil/
        // Kingfisher) will NOT rotate a second time. maxDim 2048 so nothing scales.
        // MUTATION: skip applyExifRotation (a rotated input stays rotated) → the output stays
        // landscape 400×200 → the "width < height" (upright portrait) assertion reds.
        val outcome = bridge.captureThroughProviderForTest(
            makeJpeg(400, 200, ExifInterface.ORIENTATION_ROTATE_90),
            AndroidShellBridge.CaptureOptions(maxDim = 2048, quality = 90))

        assertEquals(CameraStatus.CAPTURED, outcome.status)
        assertEquals("rotation baked: the upright width is the input HEIGHT", 200, outcome.width)
        assertEquals("rotation baked: the upright height is the input WIDTH", 400, outcome.height)
        assertTrue("the output is upright PORTRAIT (rotation baked into the pixels)",
            outcome.width < outcome.height)

        // …AND the tag is IDENTITY on the output file, so Coil does not rotate again.
        val orientation = ExifInterface(outcome.path!!.removePrefix("file://"))
            .getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_UNDEFINED)
        assertTrue("the output EXIF orientation is reset to identity (was $orientation), NOT " +
            "ROTATE_90 — else Coil/Kingfisher would double-rotate",
            orientation == ExifInterface.ORIENTATION_NORMAL ||
                orientation == ExifInterface.ORIENTATION_UNDEFINED)
    }

    // ── The prune backstop keeps only the last N captures ────────────────────

    @Test
    fun prune_keeps_only_the_last_n_captures() {
        // Six captures, then prune → only the most recent CAPTURE_KEEP_LAST (3) survive.
        // MUTATION: skip the prune → all six remain → this reds. (3 mirrors AndroidShellBridge's
        // CAPTURE_KEEP_LAST — a private const; the count is asserted here.)
        repeat(6) {
            bridge.captureThroughProviderForTest(
                makeJpeg(64, 64), AndroidShellBridge.CaptureOptions(maxDim = 256, quality = 80))
        }
        assertEquals("six captures were written", 6, bridge.captureFileCountForTest())

        bridge.pruneCaptureDir()
        assertEquals("the prune backstop keeps the last 3", 3, bridge.captureFileCountForTest())
    }

    // ── `check` reports availability as DATA ─────────────────────────────────

    @Test
    fun check_reports_camera_availability_as_data() {
        // The AVD (blazornative-pixel6-x86_64) has an emulated back camera and the manifest
        // <queries> for IMAGE_CAPTURE, so a system camera app resolves → Captured ("present +
        // usable"). MUTATION: drop the <queries> → resolveActivity returns null under API 30+
        // package visibility → Unavailable. Either way it is a CameraStatus VALUE, never a throw.
        val status = bridge.cameraAvailabilityStatus()
        assertTrue("check must return a CameraStatus value (0..4), got $status",
            status in CameraStatus.CAPTURED..CameraStatus.ERROR)
        assertEquals("the AVD's emulated camera resolves ACTION_IMAGE_CAPTURE via <queries>",
            CameraStatus.CAPTURED, status)
    }

    /** A solid-colour JPEG of the given pixel size, optionally tagged with an EXIF orientation —
     * the synthetic "scene" the seam writes through the FileProvider (the emulator's own camera
     * feed is un-rotated, so a rotated input is fabricated here to exercise the normalizer). */
    private fun makeJpeg(width: Int, height: Int, exifOrientation: Int = ExifInterface.ORIENTATION_NORMAL): ByteArray {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        Canvas(bmp).drawColor(Color.rgb(10, 120, 200))
        val tmp = File.createTempFile("scene", ".jpg", ctx.cacheDir)
        FileOutputStream(tmp).use { bmp.compress(Bitmap.CompressFormat.JPEG, 95, it) }
        bmp.recycle()
        if (exifOrientation != ExifInterface.ORIENTATION_NORMAL) {
            ExifInterface(tmp.absolutePath).apply {
                setAttribute(ExifInterface.TAG_ORIENTATION, exifOrientation.toString())
                saveAttributes()
            }
        }
        val bytes = tmp.readBytes()
        tmp.delete()
        return bytes
    }
}

/** The composition half — camera driven through BnCameraDemo (the real .NET round-trip), with
 * the real system camera-app shutter bypassed via [AndroidShellBridge.cameraCaptureHook] (the
 * geolocation/notifications/biometrics real-UI split). Proves the capture → file → BnImage
 * composition, and denial-as-data (Cancelled, no hang). */
@RunWith(AndroidJUnit4::class)
class BnCameraAndroidTest {

    @Before
    fun reset() {
        AndroidShellBridge.resetCameraForTest()
    }

    @After
    fun cleanup() {
        AndroidShellBridge.resetCameraForTest()
    }

    // ── THE COMPOSITION: capture → file → a MEASURED, PAINTED BnImage ─────────

    @Test
    fun take_photo_composes_a_captured_file_into_a_measured_bnimage() {
        // The seam writes a synthetic 640×480 scene THROUGH the FileProvider URI and delivers
        // RESULT_OK — the shell processes it, returns the file:// path, and the demo sets it as
        // the BnImage's Src. The composition: camera → a real file with bytes → BnImage loads +
        // measures it. MUTATION: mis-authority the FileProvider → the capture errors → the echo
        // never reads "captured:" → this reds.
        AndroidShellBridge.cameraCaptureHook = { capture -> capture.captureScene(makeJpeg(640, 480)) }

        CameraHarness.launchDemo().use { scenario ->
            assertNotNull("BnCameraDemo never rendered within 60s", CameraHarness.pollForProbe(scenario))

            CameraHarness.tapButton(scenario, "Take Photo")
            assertTrue("Take Photo never echoed a 'captured:' result within 15s (the capture " +
                "never completed — a HANG, or the FileProvider write failed)",
                CameraHarness.pollTrue(15_000) {
                    CameraHarness.echoTextOn(scenario)?.startsWith("captured:") == true
                })

            // The echo carries the FINAL dims + byte size — proof the file the path names has bytes.
            val echo = CameraHarness.echoTextOn(scenario)!!
            val dims = echo.removePrefix("captured:").split(":")
            val wh = dims[0].split("x")
            assertTrue("captured width > 0 (echo: $echo)", wh[0].toInt() > 0)
            assertTrue("captured height > 0 (echo: $echo)", wh[1].toInt() > 0)
            assertTrue("captured byte size > 0 (echo: $echo)", dims[1].toLong() > 0)

            // THE COMPOSITION, on screen: the BnImage measures at the demo's DEFINITE 240×320 box
            // (a multi-megapixel photo cannot reflow the layout — the M6/M7 ledger discharge) and
            // PAINTS the loaded local file (Coil loaded the file:// path).
            assertTrue("the captured file never loaded into the BnImage within 15s (Coil did not " +
                "load the local file:// path)",
                CameraHarness.pollTrue(15_000) { CameraHarness.imageDrawableOn(scenario) != null })

            scenario.onActivity { act ->
                val image = CameraHarness.imageIn(act)!!
                val density = act.resources.displayMetrics.density
                assertEquals("the display BnImage measures at its DEFINITE 240dp width",
                    240f, image.width / density, 1f)
                assertEquals("…and its DEFINITE 320dp height (never measured → no reflow)",
                    320f, image.height / density, 1f)
                assertNotNull("…and it PAINTED the captured file (the composition)", image.drawable)
            }
        }
    }

    // ── A Cancelled capture is DATA within a bounded await — NO HANG ──────────

    @Test
    fun capture_cancelled_is_data_within_a_bounded_await_no_hang() {
        // The seam drives a cancel (the user backed out). If the cancel path threw or dropped the
        // completion, the echo would stay blank forever — a HANG. It resolves to DATA (no path).
        AndroidShellBridge.cameraCaptureHook = { capture -> capture.cancel() }

        CameraHarness.launchDemo().use { scenario ->
            assertNotNull("BnCameraDemo never rendered within 60s", CameraHarness.pollForProbe(scenario))

            CameraHarness.tapButton(scenario, "Take Photo")
            assertTrue("a cancelled capture never reached the echo within 10s (a HANG — cancel " +
                "was not data)",
                CameraHarness.pollTrue(10_000) {
                    CameraHarness.echoTextOn(scenario) == "status:Cancelled"
                })
            // …and no path was set, so the BnImage painted nothing.
            scenario.onActivity { act ->
                assertNull("a Cancelled capture carries NO path — the BnImage stays empty",
                    CameraHarness.imageIn(act)?.drawable)
            }
        }
    }

    // ── `check` reports availability as data (no camera UI launched) ─────────

    @Test
    fun check_reports_availability_as_data() {
        // No hook needed — check never launches the camera UI. On the AVD (emulated camera +
        // <queries>) it resolves Captured (available). MUTATION: drop the <queries> → Unavailable.
        CameraHarness.launchDemo().use { scenario ->
            assertNotNull("BnCameraDemo never rendered within 60s", CameraHarness.pollForProbe(scenario))

            CameraHarness.tapButton(scenario, "Check")
            assertTrue("Check never echoed an availability status within 10s",
                CameraHarness.pollTrue(10_000) {
                    CameraHarness.echoTextOn(scenario) == "status:Captured"
                })
        }
    }

    private fun makeJpeg(width: Int, height: Int): ByteArray {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        Canvas(bmp).drawColor(Color.rgb(200, 90, 30))
        val tmp = File.createTempFile("scene", ".jpg", ctx.cacheDir)
        FileOutputStream(tmp).use { bmp.compress(Bitmap.CompressFormat.JPEG, 95, it) }
        bmp.recycle()
        val bytes = tmp.readBytes()
        tmp.delete()
        return bytes
    }
}

/** Shared launch/poll/tap harness (the SecureHarness house style) for BnCameraDemo. */
private object CameraHarness {

    fun launchDemo(): ActivityScenario<MainActivity> {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnCameraDemo")
        return ActivityScenario.launch(intent)
    }

    /** The demo's root div: widget_root's single child once mounted. */
    private fun probeRoot(act: MainActivity): ViewGroup? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? ViewGroup

    /** The echo TextView: the root's child that is a TextView but NOT a Button. */
    private fun echoText(act: MainActivity): TextView? {
        val root = probeRoot(act) ?: return null
        for (i in 0 until root.childCount) {
            val child = root.getChildAt(i)
            if (child is TextView && child !is Button) return child
        }
        return null
    }

    fun echoTextOn(scenario: ActivityScenario<MainActivity>): String? {
        val out = AtomicReference<String?>(null)
        scenario.onActivity { act -> out.set(echoText(act)?.text?.toString()) }
        return out.get()
    }

    /** The demo's display BnImage — the one ImageView under the root. */
    fun imageIn(act: MainActivity): ImageView? {
        val root = probeRoot(act) ?: return null
        for (i in 0 until root.childCount) {
            val child = root.getChildAt(i)
            if (child is ImageView) return child
        }
        return null
    }

    fun imageDrawableOn(scenario: ActivityScenario<MainActivity>): Any? {
        val out = AtomicReference<Any?>(null)
        scenario.onActivity { act -> out.set(imageIn(act)?.drawable) }
        return out.get()
    }

    /** Polls until the demo's mount shape is on screen (2 buttons + image + echo). */
    fun pollForProbe(scenario: ActivityScenario<MainActivity>, deadlineMs: Long = 60_000): View? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<View?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = probeRoot(act)
                if (root != null && root.childCount >= 4 && echoText(act) != null && imageIn(act) != null)
                    found.set(root)
            }
            if (found.get() != null) break
            Thread.sleep(250)
        }
        return found.get()
    }

    fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            val root = act.findViewById<FrameLayout>(R.id.widget_root)
            val button = root?.let { firstMatch(it) { v -> v is Button && v.text.toString() == label } } as? Button
            if (button != null) { button.performClick(); clicked.set(true) }
        }
        assertTrue("Button '$label' not found on screen", clicked.get())
    }

    fun pollTrue(deadlineMs: Long, predicate: () -> Boolean): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            if (predicate()) return true
            Thread.sleep(200)
        }
        return predicate()
    }

    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) {
            for (i in 0 until view.childCount) firstMatch(view.getChildAt(i), predicate)?.let { return it }
        }
        return null
    }
}
