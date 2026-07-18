using System.Runtime.InteropServices;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// SecureBiometricsAbiUnchangedTests — Phase 9.2 Gate 1 (M9 DoD #4): THE HEADLINE,
// MADE FALSIFIABLE, a THIRD time. Phase 9.0 grew the ABI once, generically, on the
// bet that 9.1/9.2/9.3 add an op constant and touch the ABI at NOTHING else. 9.1
// was the first draw and held it; 9.2 is the second draw — TWO capabilities at once
// (biometrics + a secret store, one of which returns a payload and one of which
// prompts). This file asserts, in the phase's own suite, that adding both moved the
// ABI at nothing:
//
//   · the bridge struct is STILL 80 bytes / 10 slots (no grow);
//   · HostCallBegin is STILL at offset 72 (the last slot; the two ops add none);
//   · Biometrics rides the op-enum as value 2, SecureStorage as value 3 (wire
//     vocabulary carried on the existing `int op` field, not new slots).
//
// These duplicate BridgeProtocolNativeTests' pins DELIBERATELY (the 9.1
// NotificationsAbiUnchangedTests precedent): that file owns the ABI contract in the
// abstract; this one ties the "biometrics + secure storage add zero ABI" claim to
// the feature, so the named reuse-proof mutation ("assert 81 bytes / 11 exports →
// this reds") lands next to the code it guards. The 10-export gate is asserted by
// the CI publish step (host_call_complete present, no new symbol) — the other half
// of the same falsifiable claim, quoted UNCHANGED.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SecureBiometricsAbiUnchangedTests
{
    [Fact]
    public void TwoOps_AddNoStructGrow_BridgeStill80Bytes()
    {
        // The reuse-proof tripwire: a HostCallOp that accidentally grew the struct
        // (a new slot for biometrics/storage instead of riding HostCallBegin) reds
        // here — the named "assert 81 bytes" mutation.
        Assert.Equal(80, Marshal.SizeOf<BlazorNativeBridgeCallbacks>());
    }

    [Fact]
    public void TwoOps_AddNoNewSlot_HostCallBeginStillAtOffset72()
    {
        // Both ops reuse the EXISTING HostCallBegin slot (the last, at 72); they
        // append nothing, so 72 is unchanged and no slot moved.
        Assert.Equal(72, (int)Marshal.OffsetOf<BlazorNativeBridgeCallbacks>(
            nameof(BlazorNativeBridgeCallbacks.HostCallBegin)));
    }

    [Fact]
    public void OpConstants_AreWireVocabulary_ValuesTwoAndThree()
    {
        // The ONLY op-enum change: Biometrics = 2 and SecureStorage = 3 beside
        // Geolocation = 0 and Notifications = 1. Enum values are wire vocabulary
        // carried on the existing `int op` field — not a struct grow, not an export,
        // not a drift-pin move.
        Assert.Equal(0, (int)NativeShellBridge.HostCallOp.Geolocation);
        Assert.Equal(1, (int)NativeShellBridge.HostCallOp.Notifications);
        Assert.Equal(2, (int)NativeShellBridge.HostCallOp.Biometrics);
        Assert.Equal(3, (int)NativeShellBridge.HostCallOp.SecureStorage);
    }
}
