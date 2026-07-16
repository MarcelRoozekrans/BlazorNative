using System.Runtime.CompilerServices;
using BlazorNative.Runtime;

namespace MyBlazorNativeApp;

// ─────────────────────────────────────────────────────────────────────────────
// AppPages — YOUR APP'S MANIFEST.
//
// THE RULE, stated once: a page is declared ONCE — one row in the `All` array
// below. Everything else is a DERIVED VIEW of it: the runtime's mount registry
// and its route table are built from this array (same object graph — they
// cannot drift from it), so adding a page is one row here and nothing else on
// the .NET side.
//
// THE ONE EXCEPTION, and it is pinned: Android's
// MainActivity.DEEP_LINK_COMPONENTS (android/src/androidMain/kotlin/io/
// blazornative/shell/MainActivity.kt) is a hand-written MIRROR of this array's
// ROUTED rows. It cannot be a derived view — it is consulted at Intent-parse
// time, BEFORE the native library loads. So when you add a ROUTED page, add its
// row there too. A mirror that drifts fails no compile and no test: the deep
// link just opens the wrong screen, on Android alone, silently.
//
// WHO CALLS Init(): nobody, by name — that is the point. There is no managed
// Main in a NativeLib; [ModuleInitializer] is the one hook where app code runs
// without being named. NativeAOT compiles module initializers into the startup
// path and runs them EAGERLY at runtime initialization — inside the first
// exported call (blazornative_init), before it returns.
//
// TRIM LAW (why every row goes through the two factories): each row's mount
// thunk is `static r => r.Mount<T>()` — a statically-rooted generic
// instantiation with a CONCRETE T, so nothing goes reflective and this
// initializer is an unconditional ILC root. That holds only for modules that
// SURVIVE into the image, which is what the csproj's TrimmerRootAssembly line
// is for. Do not delete it; its comment explains what breaks.
// ─────────────────────────────────────────────────────────────────────────────

public static class AppPages
{
    /// <summary>THE manifest. Two factories:
    /// <list type="bullet">
    ///   <item><c>BlazorNativePage.Routed&lt;T&gt;(route, name)</c> — reachable by
    ///   route (navigation + Android deep links) AND by mount name.</item>
    ///   <item><c>BlazorNativePage.Named&lt;T&gt;(name)</c> — reachable by mount
    ///   name only; no route, no deep link.</item>
    /// </list>
    /// <c>BlazorNativeApp.DefaultRoute</c> ("/") is the page the app boots into.
    /// Mount values return the root componentId, and Mount&lt;T&gt; is SYNC — the
    /// first render completes before the mount call returns.</summary>
    public static readonly BlazorNativePage[] All =
    [
        BlazorNativePage.Routed<BnStarterPage>(BlazorNativeApp.DefaultRoute, "BnStarterPage"),
        // Add your next page here, e.g.:
        //   BlazorNativePage.Routed<BnAboutPage>("/about", "BnAboutPage"),
        // A ROUTED row also needs its pair in MainActivity.DEEP_LINK_COMPONENTS
        // (see the header). An unrouted probe/screen is:
        //   BlazorNativePage.Named<SomeScreen>("SomeScreen"),
    ];

    private static bool s_registered;

    /// <summary>NativeAOT compiles this into the startup path — it has run (and
    /// the registry is populated) before blazornative_init returns.</summary>
    // CA2255 ("only intended for application code"): this IS the application —
    // a NativeLib has no exe head, so OutputType stays Library and the analyzer
    // cannot see that this project is the composition root.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Init() => EnsureRegistered();
#pragma warning restore CA2255

    /// <summary>Idempotent — safe to call explicitly from a test host, where
    /// module initializers run LAZILY (on first touch of this assembly, which a
    /// mount-by-NAME test never performs). The once-guard makes the eager
    /// NativeAOT path and an explicit call meet safely.
    ///
    /// GUARD ORDER IS LOAD-BEARING: the guard is set only AFTER RegisterPages
    /// returns. A throwing registration therefore leaves it CLEAR and the next
    /// call re-throws — loud and repeatable — instead of silently no-op'ing into
    /// an EMPTY registry, which would surface as rc 1 at first mount with
    /// nothing naming the cause. Keep the assignment below the call. (The
    /// BlazorNative repo pins this order in both this file and its own sample:
    /// TemplateDriftTests.)</summary>
    public static void EnsureRegistered()
    {
        if (s_registered)
            return;
        BlazorNativeApp.RegisterPages(All);
        s_registered = true; // only after the call SUCCEEDS — see the note above
    }
}
