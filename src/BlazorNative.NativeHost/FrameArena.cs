using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BlazorNative.NativeHost;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d pooled native scratch for frame marshaling.
//
// One native block per thread, bump-pointer allocation inside it. Rent() hands
// out the thread-cached instance (allocating it on first use) and Reset()s the
// bump offset; Dispose() returns the instance to the thread cache — it does
// NOT free the native block. The cache is process-lifetime by design: the
// frame path runs at up to 60fps and must not touch the allocator (native or
// managed) per frame once warmed up. The block is intentionally leaked at
// process exit, same policy as Exports.cs's static cstrings.
//
// Growth: if a frame outsizes capacity, a doubled block is allocated and
// becomes the active block. The outgrown block is NOT freed immediately —
// pointers handed out earlier in the same frame (the encoder's patch array,
// prior strings) still point into it — it is retired and freed on the next
// Rent()/Reset(). Steady state after warmup is a single right-sized block and
// zero allocation per frame (pinned by FrameArenaTests.RentDispose_1000Frames).
//
// Alignment: the bump offset is kept 8-aligned (alloc sizes round up to 8),
// so BlazorNativePatch arrays and their IntPtr fields are naturally aligned
// even after arbitrary UTF-8 string allocs.
//
// Not thread-safe per instance; per-thread caching makes that a non-issue as
// long as a rented arena is used only on the renting thread (frame callback
// contract — the Kotlin side copies synchronously before returning).
// ─────────────────────────────────────────────────────────────────────────────

internal sealed unsafe class FrameArena : IDisposable
{
    private const nuint InitialCapacity = 16 * 1024;

    [ThreadStatic]
    private static FrameArena? t_cached;

    private byte* _block;
    private nuint _capacity;
    private nuint _offset;
    private bool _rented;
    private List<IntPtr>? _retired; // outgrown blocks, freed on next Reset()

    private FrameArena()
    {
        _block = (byte*)NativeMemory.Alloc(InitialCapacity);
        _capacity = InitialCapacity;
    }

    /// <summary>Rents the calling thread's cached arena (creating it on first
    /// use), reset and ready for one frame's worth of allocations.</summary>
    public static FrameArena Rent()
    {
        FrameArena? arena = t_cached;
        if (arena is null)
        {
            arena = new FrameArena();
        }
        else
        {
            t_cached = null; // guard against double-rent aliasing
        }

        arena.Reset();
        arena._rented = true;
        return arena;
    }

    /// <summary>Allocates a zeroed, 8-aligned array of <paramref name="count"/>
    /// patches. Valid until the next Rent() on this thread.</summary>
    public BlazorNativePatch* AllocPatches(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        nuint size = (nuint)count * (nuint)sizeof(BlazorNativePatch);
        byte* p = Alloc(size);
        NativeMemory.Clear(p, size);
        return (BlazorNativePatch*)p;
    }

    /// <summary>Copies <paramref name="s"/> as NUL-terminated UTF-8 into the
    /// arena. Null maps to IntPtr.Zero; "" yields a valid pointer to a lone
    /// NUL. Valid until the next Rent() on this thread.</summary>
    public IntPtr AllocUtf8(string? s)
    {
        if (s is null)
            return IntPtr.Zero;

        int byteCount = Encoding.UTF8.GetByteCount(s);
        byte* p = Alloc((nuint)byteCount + 1);
        Encoding.UTF8.GetBytes(s, new Span<byte>(p, byteCount));
        p[byteCount] = 0;
        return (IntPtr)p;
    }

    /// <summary>Returns this arena to the thread cache. Does NOT free the
    /// native block (process-lifetime cache — see class header). Idempotent.
    /// Thread affinity: Dispose is only a guaranteed no-op cross-thread when
    /// <c>_rented</c> is already false (a stale double-Dispose echo). Disposing
    /// a STILL-RENTED arena from a foreign thread migrates it into THAT
    /// thread's cache slot (or frees it if occupied) — a contract violation:
    /// rent, alloc, and dispose must all happen on the same thread (the frame
    /// callback runs synchronously on the renderer thread, so this holds).</summary>
    public void Dispose()
    {
        if (!_rented)
            return; // second Dispose (or stale foreign-thread echo): no-op
        _rented = false;

        if (t_cached is null)
        {
            t_cached = this;
        }
        else
        {
            // Rare: two arenas were rented on this thread (nested frames) and
            // the cache slot is already taken. Free this one instead of leaking.
            Reset();
            NativeMemory.Free(_block);
            _block = null;
            _capacity = 0;
        }
    }

    private byte* Alloc(nuint size)
    {
        // Alloc on a non-rented arena = use-after-Dispose: the next Rent()'s
        // Reset() reuses the same bytes, silently corrupting live pointers.
        Debug.Assert(_rented, "FrameArena.Alloc called on a non-rented arena (use-after-Dispose).");

        nuint advance = (size + 7) & ~(nuint)7; // keep _offset 8-aligned
        if (_offset + advance > _capacity)
            Grow(advance);

        byte* p = _block + _offset;
        _offset += advance;
        return p;
    }

    private void Grow(nuint needed)
    {
        nuint newCapacity = _capacity * 2;
        while (newCapacity < needed)
            newCapacity *= 2;

        // Retire (don't free) the outgrown block: pointers handed out earlier
        // in this frame still reference it. Freed on the next Reset().
        (_retired ??= new List<IntPtr>(capacity: 2)).Add((IntPtr)_block);

        _block = (byte*)NativeMemory.Alloc(newCapacity);
        _capacity = newCapacity;
        _offset = 0;
    }

    private void Reset()
    {
        if (_retired is { Count: > 0 })
        {
            foreach (IntPtr old in _retired)
                NativeMemory.Free((void*)old);
            _retired.Clear();
        }

        _offset = 0;
    }
}
