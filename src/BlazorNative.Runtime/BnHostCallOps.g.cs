// GENERATED FILE — DO NOT EDIT (#255).
//
// Source of truth: src/wire-vocabulary.json
// Regenerate:      dotnet run --project tools/BlazorNative.WireGen
//
// These names used to be hand-maintained in four languages, agreeing only
// because a drift test parsed them back out and said so. Editing this file
// by hand puts that back: WireVocabularyCodegenTests re-runs the emitter and
// byte-compares, so the edit fails the required build-test lane rather than
// reaching a device.

namespace BlazorNative.Runtime;

/// <summary>The op integer of the ONE hostCallBegin slot: which capability a
/// permission-gated host call is for, generated from the manifest. The integer IS
/// the wire contract: every shell switches on it, and each id is frozen once
/// shipped. Adding an op is NOT an ABI change: no slot, no export, no struct grow.</summary>
internal enum HostCallOp
{
    /// <summary>Phase 9.0. The current position (mode request) or the read-only permission check (mode check).</summary>
    Geolocation = 0,
    /// <summary>Phase 9.1. Schedule, show or cancel a local notification, or request or check the permission.</summary>
    Notifications = 1,
    /// <summary>Phase 9.2. An OS biometric prompt, or the read-only availability check.</summary>
    Biometrics = 2,
    /// <summary>Phase 9.2. Set, get, get-with-auth or delete an encrypted-at-rest secret.</summary>
    SecureStorage = 3,
    /// <summary>Phase 9.3. Capture a photo, returned as a file path, or the read-only availability check.</summary>
    Camera = 4,
    /// <summary>Phase 16.1, #8. A handler faulted after its first await, too late to be its dispatch rc 2. Args are flat JSON: handlerId (0 for a reserved host event), event, type and message; never a stack trace or an event payload, because either can carry user data. The shell routes it to onError and completes it OK with a null payload. Sent fire-and-forget: .NET ignores the result.</summary>
    FaultNotice = 5,
    /// <summary>Phase 16.2, #346. Whether .NET can go back. Args are flat JSON: canGoBack, the string true or false. Sent when the value changes and once for a session's first mount, and for a navigation BEFORE the frames that show the new page, so the shell can apply it in the same main-thread batch as that page. The shell enables or disables its back callback from it and completes it OK with a null payload. Sent fire-and-forget: .NET ignores the result.</summary>
    BackState = 6,
    /// <summary>Phase 16.2, #346. A back reached .NET and could not be handled, because it is at the root or has no session. Args are an empty flat JSON object. The shell finishes, so a back press is never swallowed, and completes it OK with a null payload. Sent fire-and-forget: .NET ignores the result.</summary>
    BackUnhandled = 7,
}
