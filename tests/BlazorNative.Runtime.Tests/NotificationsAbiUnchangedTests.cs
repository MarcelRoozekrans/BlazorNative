using System.Runtime.InteropServices;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NotificationsAbiUnchangedTests — Phase 9.1 Gate 1 (M9 DoD #3): THE HEADLINE,
// MADE FALSIFIABLE. Phase 9.0 grew the ABI once, generically, on the bet that
// 9.1/9.2/9.3 add an op constant and touch the ABI at NOTHING else. 9.1 is the
// first reuse — so this file asserts, in the notifications suite itself, that
// adding local notifications moved the ABI at nothing:
//
//   · the bridge struct is STILL 80 bytes / 10 slots (no grow);
//   · HostCallBegin is STILL at offset 72 (the last slot; notifications add none);
//   · Notifications rides the op-enum as value 1 (wire vocabulary, not a slot).
//
// These duplicate BridgeProtocolNativeTests' pins DELIBERATELY: that file owns the
// ABI contract in the abstract; this one ties the "notifications add zero ABI"
// claim to the notifications feature, so the named reuse-proof mutation ("assert
// 81 bytes / a moved offset → this reds") lands here, next to the code it guards.
// The 10-export gate is asserted by the CI publish step (host_call_complete
// present, no new symbol) — the other half of the same falsifiable claim.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NotificationsAbiUnchangedTests
{
    [Fact]
    public void Notifications_AddNoStructGrow_BridgeStill80Bytes()
    {
        // The reuse-proof tripwire: a HostCallOp that accidentally grew the struct
        // (a new slot for notifications instead of riding HostCallBegin) reds here.
        Assert.Equal(80, Marshal.SizeOf<BlazorNativeBridgeCallbacks>());
    }

    [Fact]
    public void Notifications_AddNoNewSlot_HostCallBeginStillAtOffset72()
    {
        // Notifications reuse the EXISTING HostCallBegin slot (the last, at 72);
        // they append nothing, so 72 is unchanged and no slot moved.
        Assert.Equal(72, (int)Marshal.OffsetOf<BlazorNativeBridgeCallbacks>(
            nameof(BlazorNativeBridgeCallbacks.HostCallBegin)));
    }

    [Fact]
    public void Notifications_OpConstant_IsWireVocabulary_ValueOne()
    {
        // The ONLY op-enum change: Notifications = 1 beside Geolocation = 0. An
        // enum value is wire vocabulary carried on the existing `int op` field —
        // not a struct grow, not an export, not a drift-pin move.
        Assert.Equal(0, (int)NativeShellBridge.HostCallOp.Geolocation);
        Assert.Equal(1, (int)NativeShellBridge.HostCallOp.Notifications);
    }
}
