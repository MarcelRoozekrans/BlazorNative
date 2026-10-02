namespace BlazorNative.Renderer;

/// <summary>How a dispatch's synchronous part ended (Phase 16.1). See
/// <see cref="NativeRenderer.DispatchSyncPart"/>.</summary>
internal enum DispatchOutcomeKind
{
    /// <summary>The handler ran to completion without faulting, or the handler id was
    /// stale. Export rc 0.</summary>
    Completed,

    /// <summary>The handler, its re-render or frame delivery faulted before the handler
    /// yielded. Export rc 2.</summary>
    Faulted,

    /// <summary>The synchronous part faulted AFTER the handler began a host call or a fetch
    /// (16.7, #455). Export rc 0; the fault goes to the shell as a FaultNotice, exactly as
    /// a fault after a yield does, so the outcome no longer depends on whether the shell
    /// answered inside begin.</summary>
    FaultedAfterShellCall,

    /// <summary>The handler yielded on an await and is still running. Export rc 0; the
    /// pending Task goes to fault delivery.</summary>
    Pending,
}

/// <summary>The result of <see cref="NativeRenderer.DispatchSyncPart"/>:
/// <paramref name="Fault"/> is set for <see cref="DispatchOutcomeKind.Faulted"/> and
/// <see cref="DispatchOutcomeKind.FaultedAfterShellCall"/>, and
/// <paramref name="Pending"/>, the handler's still-running Task, for
/// <see cref="DispatchOutcomeKind.Pending"/>.</summary>
internal readonly record struct DispatchOutcome(DispatchOutcomeKind Kind, Exception? Fault, Task? Pending);
