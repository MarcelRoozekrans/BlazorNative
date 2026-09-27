package io.blazornative.jni

import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

/**
 * Phase 16.1 test support for the late-continuation pins ([ShutdownQuiescenceTest],
 * [RetireLateContinuationTest]): a host whose camera op records the request and NEVER
 * completes it, the same shape as HostEventTest's StallingCameraHost. The call stays
 * open, as it does while an OS permission sheet is up, until the test completes it
 * through the real `blazornative_host_call_complete` export ([release]).
 */
internal class HeldCameraHost : ShellBridgeHandlers {
    @Volatile private var route: String = "/"
    val callReceived = CountDownLatch(1)
    @Volatile var heldRequestId: Long = -1
    override fun navigate(route: String) { this.route = route }
    override fun currentRoute(): String = route
    override fun storageRead(key: String): String? = null
    override fun storageWrite(key: String, value: String) {}
    override fun storageDelete(key: String) {}
    override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
        BridgeFetchCompleter.completeFailure(requestId, "HeldCameraHost performs no fetch")
    }
    override fun clipboardRead(): String = ""
    override fun clipboardWrite(text: String) {}
    override fun share(text: String) {}
    override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
        heldRequestId = requestId
        callReceived.countDown() // NOT completed: the test releases it
    }

    /** Completes the held call as Cancelled; the demo's continuation then echoes
     * "status:Cancelled". Returns the export's rc, or null when no call was held. */
    fun release(): Int? =
        heldRequestId.takeIf { it >= 0 }?.let {
            BridgeHostCallCompleter.complete(it, CameraStatus.CANCELLED, null)
        }
}

/** The text the BnCameraDemo echo shows once the held call is released as Cancelled. */
internal const val CANCELLED_ECHO = "status:Cancelled"

internal fun RenderFrame.hasText(text: String): Boolean =
    patches.filterIsInstance<RenderPatch.ReplaceText>().any { it.text == text }

/** The click handler of BnCameraDemo's "Take Photo" button, harvested structurally from
 * the mount frame (the HostEventTest convention: text node → its parent → its click). */
internal fun takePhotoHandler(mount: RenderFrame): Int {
    val text = checkNotNull(
        mount.patches.filterIsInstance<RenderPatch.ReplaceText>().singleOrNull { it.text == "Take Photo" }
    ) { "expected exactly one ReplaceText 'Take Photo' in the BnCameraDemo mount; got ${mount.patches}" }
    val button = checkNotNull(
        mount.patches.filterIsInstance<RenderPatch.CreateNode>().single { it.nodeId == text.nodeId }.parentId
    ) { "the 'Take Photo' text node must have a parent" }
    return checkNotNull(
        mount.patches.filterIsInstance<RenderPatch.AttachEvent>()
            .singleOrNull { it.nodeId == button && it.eventName == "click" }
    ) { "expected exactly one click AttachEvent on node $button; got ${mount.patches}" }.handlerId
}

/** Waits for the held call to arrive, and fails with a message naming what the run could not measure. */
internal fun awaitHeldCall(host: HeldCameraHost) {
    check(host.callReceived.await(10, TimeUnit.SECONDS)) {
        "the camera host call never arrived, so the handler never suspended on it and this " +
            "run cannot measure a late continuation"
    }
}
