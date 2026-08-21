namespace BlazorNative.Testing;

/// <summary>Which shell's projection a <see cref="BnTestHost"/> models.</summary>
/// <remarks>The shells agree on the wire and differ in how they PROJECT it — the
/// text-collapse predicate is the case that reaches a test author. Naming the shell
/// makes an assertion true of a stated platform instead of true of one and silently
/// wrong on the other.</remarks>
public enum BnShell
{
    /// <summary>The Apple shell. The default, preserving pre-13.4 behaviour.</summary>
    Ios,

    /// <summary>The Android shell — a broader text-bearing set (checkbox, switch).</summary>
    Android,
}
