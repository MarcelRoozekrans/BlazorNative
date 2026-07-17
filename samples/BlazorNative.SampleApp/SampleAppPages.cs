using System.Runtime.CompilerServices;
using BlazorNative.Runtime;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// SampleAppPages — Phase 8.0 (design decisions 1+2, M8 DoD #1: the
// registration inversion).
//
// THE APP'S MANIFEST: the 14 rows (9 routed pages + 5 unrouted test probes)
// that lived in Runtime's PageManifest.cs until 8.0, provenance comments and
// all — now declared by the app that owns them and PUSHED through the public
// BlazorNativeApp.RegisterPages API. The 7.6 normative rule holds verbatim
// with the owner swapped: a page is declared ONCE — one row in THIS array;
// Runtime's store and its two derived views (mount registry, route table)
// cannot drift from it by construction; Android's DEEP_LINK_COMPONENTS
// remains the one PINNED MIRROR, drift-tested pair-for-pair against this
// array by RouteTableDriftTests in the required build-test lane.
//
// WHO CALLS Init(): nobody, by name — that is the point. There is no managed
// Main in a NativeLib; [ModuleInitializer] is the one hook where app code
// runs without being named. NativeAOT compiles module initializers into the
// startup path and runs them EAGERLY at runtime initialization — inside the
// first exported call (blazornative_init), before it returns. CoreCLR test
// hosts run them LAZILY (on first touch of this assembly, which a
// mount-by-NAME test never performs), so Runtime.Tests calls the idempotent
// EnsureRegistered() from its own module initializer instead.
//
// `All` is deliberately PUBLIC: it is the drift test's target, the 8.3
// template's worked example, and sample apps are documentation.
//
// TRIM LAW (why every row goes through the two factories): each row's mount
// thunk is `static r => r.Mount<T>()` — a statically-rooted generic
// instantiation with a CONCRETE T, satisfying Mount<T>'s DAM(All) requirement
// at compile time. Nothing goes reflective; this initializer is an
// unconditional ILC root. The publish gates assert the 4 accepted IL2072s
// (Renderer internals — none from these rows) and 10 exports, as always.
// ─────────────────────────────────────────────────────────────────────────────

public static class SampleAppPages
{
    /// <summary>THE manifest: 10 routed pages + 5 unrouted test probes.
    /// Sync Mount&lt;T&gt; (inline dispatcher, Phase 2.4) — the first render
    /// completes before TryMount returns, so the frame callback has already
    /// fired. Mount values return the root componentId (Phase 3.5: the
    /// navigation swap needs it).</summary>
    public static readonly BlazorNativePage[] All =
    [
        BlazorNativePage.Named<HelloComponent>("HelloComponent"),
        // Phase 3.3: the composition proof app (nested components, keyed
        // list mutation, detach — design §6).
        BlazorNativePage.Named<CompositionProbe>("CompositionProbe"),
        // Phase 3.4: the Bn* demo form (bind loop + cascading theme —
        // DoD #5/#6); MainActivity's default since Gate 4. Phase 3.5 made it
        // the DEFAULT ROUTE's page.
        BlazorNativePage.Routed<BnDemo>(BlazorNativeApp.DefaultRoute, "BnDemo"),
        // Phase 3.5: the demo's second page (route "/settings" — DoD #7).
        BlazorNativePage.Routed<BnSettingsPage>("/settings", "BnSettingsPage"),
        // Phase 6.1: the flexbox proof page (route "/layout" — M6 DoD #2/#3).
        // A THIRD page on purpose: BnDemo/BnSettingsPage keep their goldens, so
        // layout bugs never arrive mixed with golden-rewrite noise. Its computed
        // frames are asserted identically on the AVD and the iOS simulator —
        // see BnLayoutDemo.razor's frame table. Its "← Back" navigates to "/";
        // nothing on BnDemo links here (a "Layout →" button would churn
        // BnDemo's goldens for no engine reason) — the shells reach it by mount
        // name (Intent extra) or by deep link.
        BlazorNativePage.Routed<BnLayoutDemo>("/layout", "BnLayoutDemo"),
        // Phase 6.2: the scrolling proof page (route "/scroll" — M6 DoD #4). A
        // FOURTH page, same rationale: BnLayoutDemo's 22-number frame table IS
        // the parity contract, and wrapping it in a scroll view would rewrite it
        // in the same phase that introduces the scroll engine. Its content size
        // (800) and row frames are asserted identically on both devices — see
        // BnScrollDemo.razor's frame table.
        BlazorNativePage.Routed<BnScrollDemo>("/scroll", "BnScrollDemo"),
        // Phase 6.3: the image proof page (route "/image" — M6 DoD #5). A FIFTH
        // page, same rationale a fourth time: the existing frame tables ARE the
        // parity contract, and a new capability does not get to rewrite them.
        // Its THREE measurement paths — fixed (never measured), intrinsic
        // (0×0 → the natural size, and the sibling below it MOVES) and failure
        // (0×0 forever, reserving nothing) — are asserted identically on the
        // AVD (Coil) and the iOS simulator (Kingfisher). See BnImageDemo.razor's
        // TWO frame tables: before the bytes land, and after.
        BlazorNativePage.Routed<BnImageDemo>("/image", "BnImageDemo"),
        // Phase 7.2: the virtualized-list proof page (route "/list" — M7
        // DoD #3). A SIXTH page, same rationale a fifth time: BnScrollDemo's
        // frame table IS the 6.2 parity contract and the phase that introduces
        // virtualization does not get to rewrite it. Its liveness counts
        // (2 spacers + 11/15/11 window rows), spacer heights and content size
        // (32,000) are asserted identically on both devices — see
        // BnListDemo.razor's header.
        BlazorNativePage.Routed<BnListDemo>("/list", "BnListDemo"),
        // Phase 7.3: the form-controls proof page (route "/form" — M7 DoD #4).
        // A SEVENTH page, same rationale a sixth time. Each control appears
        // TWICE (bound + disabled) with a live echo; the bind round-trips, the
        // loop guards, the picker clamp rule and the declared widths are
        // asserted identically on both devices — see BnFormDemo.razor.
        BlazorNativePage.Routed<BnFormDemo>("/form", "BnFormDemo"),
        // Phase 7.4: the overlay proof page (route "/modal" — M7 DoD #5).
        // An EIGHTH page, same rationale a seventh time. The modal sits BETWEEN
        // two declared-size siblings (the anchor's zero-footprint rule as a
        // frame assertion), the switch + echo prove the wire INSIDE the
        // overlay, and the indicator appears in both hosting contexts — see
        // BnModalDemo.razor's header.
        BlazorNativePage.Routed<BnModalDemo>("/modal", "BnModalDemo"),
        // Phase 7.5: the image-polish proof page (route "/imagepolish" — M7
        // DoD #6). A NINTH page, same rationale an eighth time — and here at
        // its sharpest: BnImageDemo's two frame tables ARE the 6.3 parity
        // contract and its section arithmetic is golden-pinned, so the phase
        // that adds placeholder/error/mode does NOT extend "/image". The new
        // page re-runs 6.3's measurement proofs WITH the new features present —
        // placeholder never measures (both sides), failure keeps a declared
        // box, the mode quartet's four identical frames — plus the counted
        // OnError round trip. See BnImagePolishDemo.razor's header.
        BlazorNativePage.Routed<BnImagePolishDemo>("/imagepolish", "BnImagePolishDemo"),
        // Phase 9.0: the geolocation proof page (route "/geolocation" — M9 DoD #2).
        // A TENTH routed page — but a DIFFERENT kind: the worked example of the
        // PERMISSION PATTERN, not a golden frame table. App code injects the
        // IGeolocation FACADE (the 7th package) and the whole permission dance runs
        // host-side; denial arrives as DATA and is echoed, never thrown, never hung.
        // The .NET/wire half lives here; Gates 2/3 wire the shells (AVD
        // LocationManager + requestPermissions; iOS CLLocationManager).
        BlazorNativePage.Routed<BnGeolocationDemo>("/geolocation", "BnGeolocationDemo"),
        // Phase 4.2: the focus/blur proof app (BnInput OnFocus/OnBlur →
        // echo BnText — M4 DoD #4). Scaffolding, like CompositionProbe.
        BlazorNativePage.Named<FocusProbe>("FocusProbe"),
        // Phase 5.1: the host-event proof app (IMobileBridge.NativeEvents →
        // echo BnText — M5 DoD #5). Scaffolding, like FocusProbe.
        BlazorNativePage.Named<HostEventProbe>("HostEventProbe"),
        // Phase 5.4: the clipboard/share proof app (IMobileBridge clipboard
        // read/write + share → echo BnText — M5 DoD #6). Scaffolding, like
        // HostEventProbe.
        BlazorNativePage.Named<ClipboardProbe>("ClipboardProbe"),
    ];

    private static bool s_registered;

    /// <summary>NativeAOT compiles this into the startup path — it has run
    /// (and the registry is populated) before blazornative_init returns.</summary>
    // CA2255 ("only intended for application code"): this IS the application —
    // a NativeLib has no exe head, so OutputType stays Library and the analyzer
    // cannot see that this project is the composition root (the design's
    // decision 2: the one hook where app code runs without being named).
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Init() => EnsureRegistered();
#pragma warning restore CA2255

    /// <summary>Idempotent — tests on CoreCLR call this explicitly from their
    /// own module initializer (CoreCLR runs module initializers lazily, on
    /// first touch of the assembly, which a mount-by-NAME test never
    /// performs). The once-guard makes the eager NativeAOT path and the
    /// explicit CoreCLR path meet safely.
    ///
    /// GUARD ORDER IS LOAD-BEARING (Phase 8.0 Gate 1 review M-1; flipped in
    /// 8.3, the phase that copies this pattern into the template). The guard
    /// is set only AFTER RegisterPages returns, so a throwing registration
    /// leaves it CLEAR and the next call re-throws — loud and repeatable —
    /// instead of silently no-op'ing into an empty registry, which surfaces
    /// as rc 1 at first mount with nothing naming the cause. Safe in both
    /// directions: RegisterPages validates before it registers (a validation
    /// throw registers nothing), and if it ever threw AFTER registering, the
    /// retry meets the register-once law and throws again. Every path is
    /// loud — that is the whole improvement. Behaviour-identical on every
    /// green path (this manifest never throws).
    ///
    /// The order is PINNED, in both copies — this one and the template's
    /// AppPages.cs — by TemplateDriftTests.EnsureRegistered_SetsTheGuard_
    /// AfterTheRegisterCall_InBothCopies. It is a SOURCE-ORDER pin: there is
    /// no seam to inject a throwing manifest into a static once-guard over a
    /// static array, so the pin proves the ORDER, not the semantics. Here the
    /// semantics IS the order.</summary>
    public static void EnsureRegistered()
    {
        if (s_registered)
            return;
        BlazorNativeApp.RegisterPages(All);
        s_registered = true; // only after the call SUCCEEDS — see the note above
    }
}
