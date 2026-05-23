// ─────────────────────────────────────────────────────────────────────────────
// BlazorInterop.cs — SINGLE SEAM between BlazorNative.Renderer and the internal
// layout of Microsoft.AspNetCore.Components.
//
// Bound to Microsoft.AspNetCore.Components 10.0.* — bump BlazorCompatVersion
// and re-verify accessor field names against the linked Blazor source revision
// before any major-version package upgrade.
//
// See docs/plans/2026-05-23-renderer-internal-api-design.md for rationale.
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorNative.Renderer;

internal static class BlazorInterop
{
    public static readonly Version BlazorCompatVersion = new(10, 0);

    static BlazorInterop()
    {
        VerifyVersion();
        VerifyAccessors();
    }

    private static void VerifyVersion()
    {
        var actual = typeof(Renderer).Assembly.GetName().Version;
        if (actual is null
            || actual.Major != BlazorCompatVersion.Major
            || actual.Minor != BlazorCompatVersion.Minor)
        {
            throw new BlazorVersionMismatchException(
                $"BlazorNative.Renderer expects Microsoft.AspNetCore.Components " +
                $"{BlazorCompatVersion.Major}.{BlazorCompatVersion.Minor}.* — " +
                $"found {actual?.ToString() ?? "<unknown>"}. " +
                $"Update BlazorInterop.cs (see file header) or pin the package.");
        }
    }

    private static void VerifyAccessors()
    {
        // Each accessor in this file should be touched once against a
        // default-initialised struct. Populated as accessors are added in Tasks 8-11.
        // Each touch wraps in try/catch and aggregates MissingFieldException /
        // MissingMethodException into a single BlazorVersionMismatchException.
    }
}

public sealed class BlazorVersionMismatchException : Exception
{
    public BlazorVersionMismatchException(string message) : base(message) { }
}
