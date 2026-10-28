using BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 18.1, #564: the child process behind BnLogExitDrainTests.
//
// It replaces Console.Error with a writer that takes WriteDelay to write each
// line, logs one BnLog line, waits until BnLog's drain thread is inside that
// write, and then ends the process in the way its argument names:
//
//   exit       Main returns, so AppDomain.ProcessExit is raised;
//   unhandled  Main throws, so AppDomain.UnhandledException is raised.
//
// The drain thread is a background thread, so the process does not wait for it.
// The line reaches the real stderr only if a hook waits for the drain. WriteDelay
// is well inside the hooks' 500 ms budget, and well past the few milliseconds a
// process takes to end, so the outcome does not depend on timing luck.
//
// stdout carries one marker line, `drain-in-write`, once the drain is inside the
// slow write, so the fact can tell "the hook did not drain" from "the probe never
// got that far".
// ─────────────────────────────────────────────────────────────────────────────

const string Line = "logged-before-exit";
TimeSpan writeDelay = TimeSpan.FromMilliseconds(200);

string mode = args.Length == 1 ? args[0] : "";
if (mode is not ("exit" or "unhandled"))
{
    Console.Out.WriteLine("usage: BlazorNative.LogExitProbe exit|unhandled");
    return 2;
}

var slow = new SlowWriter(Console.Error, writeDelay);
Console.SetError(slow);
BnLog.Error("LogExitProbe", Line);

if (!slow.Entered.Wait(TimeSpan.FromSeconds(10)))
{
    Console.Out.WriteLine("drain-never-entered");
    return 3;
}
Console.Out.WriteLine("drain-in-write");
Console.Out.Flush();

if (mode == "unhandled")
    throw new InvalidOperationException("LogExitProbe: an unhandled exception, on purpose");
return 0;

/// <summary>Writes each line to <paramref name="inner"/> only after <paramref name="delay"/>,
/// the way a stderr whose reader lags holds its writer.</summary>
internal sealed class SlowWriter(TextWriter inner, TimeSpan delay) : TextWriter
{
    public ManualResetEventSlim Entered { get; } = new(false);

    public override System.Text.Encoding Encoding => inner.Encoding;

    public override void WriteLine(string? value)
    {
        Entered.Set();
        Thread.Sleep(delay);
        inner.WriteLine(value);
        inner.Flush();
    }

    public override void Write(char value) => inner.Write(value);
}
