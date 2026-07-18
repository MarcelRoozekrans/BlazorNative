using System.Runtime.InteropServices;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// CameraAbiUnchangedTests — Phase 9.3 Gate 1 (M9 DoD #5): THE HEADLINE, MADE
// FALSIFIABLE, a FOURTH time. Phase 9.0 grew the ABI once, generically, on the bet
// that 9.1/9.2/9.3 add an op constant and touch the ABI at NOTHING else. 9.1 held it,
// 9.2 held it (twice at once), and 9.3 is the fourth draw — camera photo capture,
// whose RESULT is an IMAGE potentially megabytes in size. The strong prior is it holds
// again, and it does BECAUSE THE IMAGE CROSSES AS A PATH, not bytes: the completion
// payload names a temp file, the bytes never touch the wire, and so no binary/buffer
// export is added and the struct does not grow. This file asserts, in the phase's own
// suite, that adding camera moved the ABI at nothing:
//
//   · the bridge struct is STILL 80 bytes / 10 slots (no grow);
//   · HostCallBegin is STILL at offset 72 (the last slot; the op adds none);
//   · Camera rides the op-enum as value 4 (wire vocabulary carried on the existing
//     `int op` field, not a new slot).
//
// These duplicate BridgeProtocolNativeTests' pins DELIBERATELY (the 9.1/9.2
// precedent): that file owns the ABI contract in the abstract; this one ties the
// "camera adds zero ABI" claim to the feature, so the named reuse-proof mutation
// ("assert 81 bytes / 11 exports → this reds") lands next to the code it guards. The
// 10-export gate is asserted UNCHANGED by the CI publish step (host_call_complete
// present, NO new symbol — camera adds no export) — the other half of the same
// falsifiable claim.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CameraAbiUnchangedTests
{
    [Fact]
    public void Camera_AddsNoStructGrow_BridgeStill80Bytes()
    {
        // The reuse-proof tripwire: a HostCallOp that accidentally grew the struct (a
        // new slot for camera, or a binary-blob export's slot, instead of riding
        // HostCallBegin with a PATH payload) reds here — the named "assert 81 bytes"
        // mutation. This is the phase's headline tripwire: the image is large, but the
        // MESSAGE is not, so the struct does not move.
        Assert.Equal(80, Marshal.SizeOf<BlazorNativeBridgeCallbacks>());
    }

    [Fact]
    public void Camera_AddsNoNewSlot_HostCallBeginStillAtOffset72()
    {
        // Camera reuses the EXISTING HostCallBegin slot (the last, at 72); it appends
        // nothing, so 72 is unchanged and no slot moved.
        Assert.Equal(72, (int)Marshal.OffsetOf<BlazorNativeBridgeCallbacks>(
            nameof(BlazorNativeBridgeCallbacks.HostCallBegin)));
    }

    [Fact]
    public void Camera_OpConstant_IsWireVocabulary_ValueFour()
    {
        // The ONLY op-enum change: Camera = 4 beside Geolocation = 0, Notifications = 1,
        // Biometrics = 2, SecureStorage = 3. An enum value is wire vocabulary carried on
        // the existing `int op` field — not a struct grow, not an export, not a
        // drift-pin move.
        Assert.Equal(0, (int)NativeShellBridge.HostCallOp.Geolocation);
        Assert.Equal(1, (int)NativeShellBridge.HostCallOp.Notifications);
        Assert.Equal(2, (int)NativeShellBridge.HostCallOp.Biometrics);
        Assert.Equal(3, (int)NativeShellBridge.HostCallOp.SecureStorage);
        Assert.Equal(4, (int)NativeShellBridge.HostCallOp.Camera);
    }
}
