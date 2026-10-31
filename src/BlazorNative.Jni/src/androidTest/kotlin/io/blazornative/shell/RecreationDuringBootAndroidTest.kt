package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import io.blazornative.jni.BlazorNativeRuntime
import org.junit.After
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.CountDownLatch
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 18.4 (#511) — an Activity recreated while the first Activity's boot is still running.
 *
 * Each MainActivity boots on its own thread. Before 18.4 a recreation during boot let both boot
 * threads run start() in either order, so the older one could register its frame callback and
 * mount its root after the newer one: the recreated Activity then stayed blank or dead, and the
 * older runtime was never retired, because its Activity had not booted when it was destroyed.
 * Since 18.4 the newer boot thread joins the older one, retires its runtime, and only then calls
 * start(). These pins force the bad interleaving through MainActivity.bootEntryHookForTest, which
 * holds the first boot at the boot thread's entry, before the join, until the test releases it.
 *
 * A held boot must never outlive its test: the next Activity's boot would join it forever.
 * `@After` releases the latch, clears the hook and joins the held thread, whatever the outcome.
 *
 * DOES NOT COVER:
 *  - the iOS shell, which is not affected: it never recreates its root view controller and boots
 *    its runtime once per process;
 *  - a second live singleTop instance, from a deep link in another task, whose boot retires the
 *    first, still live, instance's runtime. Design 18.4 D1 accepts that with no pin: since 17.3
 *    the second mount already replaced the first instance's root, so its UI is dead either way.
 */
@RunWith(AndroidJUnit4::class)
class RecreationDuringBootAndroidTest {

    /** Released in [cleanup] whatever the outcome; a test may release it earlier. */
    private val releaseFirst = CountDownLatch(1)

    /** The first runtime that reached the entry hook: the held boot's. */
    private val first = AtomicReference<BlazorNativeRuntime?>(null)

    /** The held boot thread, joined in [cleanup] so it ends before the next test boots. */
    private val heldThread = AtomicReference<Thread?>(null)

    @After
    fun cleanup() {
        releaseFirst.countDown()
        MainActivity.bootEntryHookForTest = null
        heldThread.get()?.join(30_000)
    }

    /** Holds the first runtime that reaches the boot thread's entry; every later one passes. */
    private fun holdTheFirstBoot() {
        MainActivity.bootEntryHookForTest = { runtime ->
            if (first.compareAndSet(null, runtime)) {
                heldThread.set(Thread.currentThread())
                releaseFirst.await()
            }
        }
    }

    @Test
    fun a_recreation_while_the_first_boot_is_held_shows_the_new_activitys_page() {
        holdTheFirstBoot()
        launch().use { scenario ->
            pollTrue(10_000) { first.get() != null }
            assertNotNull("anchor: the first boot reached the entry hook", first.get())

            scenario.recreate()
            val newActivity = scenario.currentActivity()
            Thread.sleep(2_000) // an unjoined boot of BnDemo completes well within this
            assertFalse("anchor: the recreated Activity booted while the first boot was held: no join",
                newActivity.bootedForTest)

            releaseFirst.countDown()
            scenario.awaitBooted()

            assertTrue("the recreated Activity never showed its page within 60s",
                pollUntil(scenario, 60_000) { hasEditText(it) })
            val typed = "booted after the held boot"
            scenario.onActivity { act -> editText(act)!!.setText(typed) }
            assertTrue("the recreated Activity's page is dead: the first boot mounted over it",
                pollUntil(scenario, 10_000) { echoText(it)?.text?.toString() == typed })

            assertTrue("the first boot's runtime was not retired before the new boot started",
                first.get()!!.retiredForTest)
            val newRuntime = AtomicReference<BlazorNativeRuntime?>(null)
            scenario.onActivity { newRuntime.set(it.runtimeForTest) }
            assertFalse("the recreated Activity's own runtime is retired: the retire hit the new boot's " +
                "runtime, not the first's", newRuntime.get()!!.retiredForTest)
            val foreign = AtomicReference(-1)
            scenario.onActivity { foreign.set(it.mapper.foreignPatchesForTest) }
            assertTrue("the recreated Activity received patches for nodes it never created",
                foreign.get() == 0)
        }
    }

    @Test
    fun a_recreation_after_a_failed_first_boot_still_retires_it() {
        // The first boot fails: an unknown component makes start() throw at mount, so its Activity
        // never sets booted. Its Activity is closed during the hold and the next launch, with a
        // valid component, is the Activity that replaces it. A recreate() would keep the first
        // launch's Intent, and with it the unknown component.
        holdTheFirstBoot()
        val firstActivity = launch("NoSuchComponent").use { failing ->
            pollTrue(10_000) { first.get() != null }
            assertNotNull("anchor: the first boot reached the entry hook", first.get())
            failing.currentActivity()
        }

        launch().use { scenario ->
            releaseFirst.countDown()
            scenario.awaitBooted()
            assertFalse("anchor: the first boot did not fail: its Activity booted", firstActivity.bootedForTest)
            assertTrue("the failed first boot's runtime was not retired before the new boot started: " +
                "a boot that threw was skipped", first.get()!!.retiredForTest)
        }
    }

    /** Launches without waiting for the boot: the first boot is held, so it never completes here. */
    private fun launch(component: String? = null): ActivityScenario<MainActivity> {
        val intent = Intent(ApplicationProvider.getApplicationContext(), MainActivity::class.java)
        if (component != null) intent.putExtra(MainActivity.EXTRA_COMPONENT, component)
        return ActivityScenario.launch<MainActivity>(intent)
    }

    private fun widgetRoot(act: MainActivity): FrameLayout? = act.findViewById(R.id.widget_root)

    private fun hasEditText(act: MainActivity): Boolean = widgetRoot(act)?.let { firstMatch(it) { v -> v is EditText } } != null

    /** BnDemo's form, as BnDemoAndroidTest reaches it: widget_root → ScrollView → content → form. */
    private fun form(act: MainActivity): ViewGroup? {
        val scroll = widgetRoot(act)?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ScrollView ?: return null
        val content = scroll.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup ?: return null
        return content.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup
    }

    /** Form child [1]: the bound EditText. */
    private fun editText(act: MainActivity): EditText? =
        form(act)?.takeIf { it.childCount >= 6 }?.getChildAt(1) as? EditText

    /** The echo TextView: form child [2], the echo panel, holds it as its single child. */
    private fun echoText(act: MainActivity): TextView? =
        (form(act)?.takeIf { it.childCount >= 6 }?.getChildAt(2) as? ViewGroup)
            ?.takeIf { it.childCount >= 1 }?.getChildAt(0) as? TextView

    private fun pollTrue(deadlineMs: Long, predicate: () -> Boolean): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            if (predicate()) return true
            Thread.sleep(100)
        }
        return predicate()
    }

    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) for (i in 0 until view.childCount) firstMatch(view.getChildAt(i), predicate)?.let { return it }
        return null
    }
}
