using System.Text;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 18.1 — #564: a stderr that stops draining.
//
// A TextWriter whose every write blocks until the test opens its gate, the way a
// write to a full stderr pipe blocks until the reader drains it. On the Windows
// runner that reader is vstest.console, and a ~4 KiB anonymous pipe filled during
// a fault burst held the render thread inside BnLog.Error past ExportBudget.
//
// It records each completed line once the gate is open, so a pin can assert that
// a line blocked behind the gate still arrives. `Entered` is set by the first write
// to reach the writer, so a pin can tell that a writer thread is now parked inside
// it.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A stderr stand-in whose writes block until <see cref="Open"/>.</summary>
internal sealed class GatedWriter : TextWriter
{
    private readonly ManualResetEventSlim _gate = new(false);
    private readonly ManualResetEventSlim _entered = new(false);
    private readonly List<string> _lines = [];
    private readonly StringBuilder _partial = new();

    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>Opens the gate: every blocked write proceeds, and later writes do not block.</summary>
    public void Open() => _gate.Set();

    /// <summary>Waits until some write has reached the writer and is parked at the gate, or has
    /// passed it.</summary>
    public bool WaitEntered(TimeSpan timeout) => _entered.Wait(timeout);

    /// <summary>Every completed line so far, in write order.</summary>
    public List<string> Lines
    {
        get { lock (_lines) return _lines.ToList(); }
    }

    /// <summary>The completed lines joined with newlines.</summary>
    public string Text => string.Join("\n", Lines);

    public override void Write(char value)
    {
        Enter();
        lock (_lines) Append(value);
    }

    public override void Write(string? value)
    {
        Enter();
        lock (_lines)
            foreach (char c in value ?? "")
                Append(c);
    }

    public override void WriteLine(string? value)
    {
        Enter();
        lock (_lines)
        {
            _partial.Append(value);
            _lines.Add(_partial.ToString());
            _partial.Clear();
        }
    }

    private void Enter()
    {
        _entered.Set();
        _gate.Wait();
    }

    private void Append(char c)
    {
        if (c == '\n')
        {
            _lines.Add(_partial.ToString());
            _partial.Clear();
        }
        else if (c != '\r')
            _partial.Append(c);
    }
}
