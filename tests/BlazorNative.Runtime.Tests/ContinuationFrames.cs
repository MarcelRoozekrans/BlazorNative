using System.Diagnostics;
using BlazorNative.Renderer;

namespace BlazorNative.Runtime.Tests;

/// <summary>16.7 (#455): a handler's continuation after an awaited host call renders AFTER
/// <c>blazornative_dispatch_event</c> returns, from the render thread, even when the shell
/// completes the call inside <c>hostCallBegin</c>. A test that reads the frame a
/// continuation renders must wait for it; reading <c>frames[^1]</c> straight after the
/// export reads the frame from before the click. Callers add frames under
/// <c>lock (frames)</c>, because the continuation's frame arrives on another thread.</summary>
internal static class ContinuationFrames
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    /// <summary>The frame count, read under the list's lock. Take it before the dispatch
    /// and pass it to <see cref="WaitFor"/>.</summary>
    public static int Count(List<RenderFrame> frames)
    {
        lock (frames) return frames.Count;
    }

    /// <summary>Waits for the first frame at index <paramref name="from"/> or later that
    /// satisfies <paramref name="match"/>, and returns it. Fails, naming
    /// <paramref name="what"/>, when no such frame arrives within <see cref="Budget"/>.</summary>
    public static RenderFrame WaitFor(List<RenderFrame> frames, int from, Func<RenderFrame, bool> match, string what)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            lock (frames)
            {
                for (int i = from; i < frames.Count; i++)
                {
                    if (match(frames[i]))
                        return frames[i];
                }
            }
            if (sw.Elapsed >= Budget)
                throw new Xunit.Sdk.XunitException(
                    $"no frame rendered {what} within {Budget.TotalSeconds:0}s of the dispatch.");
            Thread.Sleep(10);
        }
    }
}
