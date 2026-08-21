using System.Text.RegularExpressions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DeepLinkSeedDriftTests — Phase 13.4 (#282): the iOS cold-launch route seed,
// pinned from the one lane that runs on every PR.
//
// WHAT #282 WAS. `HostViewController.viewDidLoad` constructed the shell bridge
// with its default route — `AppleShellBridge.init(initialRoute:)` defaults to
// "/" — and nothing on the boot path ever replaced it. A cold
// `blazornative://` launch therefore left .NET's router believing it was at "/":
// for a route the component map could not resolve the link was discarded
// outright, and for one it COULD resolve the right page mounted while
// `GetCurrentRouteAsync` still answered "/". Android has always seeded the route
// unconditionally (`MainActivity`: `initialRoute = deepLinkRoute ?: "/"`).
//
// ⚠ WHAT THIS PIN CAN AND CANNOT ASSERT, said plainly.
//
//  · IT CANNOT assert the runtime behaviour. The obvious test — stash a route,
//    boot the host, read the bridge back — is unreachable: `viewDidLoad` returns
//    early under XCTest ON PURPOSE (`NSClassFromString("XCTestCase")`), because
//    the test bundle is HOSTED in the app and owns the single native session.
//    Reaching the seed from a test would mean restructuring the boot into an
//    injectable seam, which is a larger change than the fix it would guard.
//  · IT CAN assert the SHAPE OF THE CALL — that the bridge is still constructed
//    with an `initialRoute:` argument, and that the argument still derives from
//    the deep-link stash rather than a hardcoded string.
//
// That is weaker than an integration test and STRONGER THAN A COMMENT, which is
// the whole point: #282's fix is one expression, its justification is twenty
// lines of prose, and this phase exists precisely because an invariant that
// lives only in prose is an invariant nothing enforces. The regression this
// refuses is a plausible one — "why is this bridge constructed the long way?"
// is exactly the tidy-up that would silently restore the bug.
//
// THE MECHANISM is `NSLogDriftTests`' and `ShellStyleTableDriftTests`': a text
// scan of checkout files from the .NET suite, walking up from the test binary to
// the directory holding BlazorNative.sln. The Apple shell is built by exactly one
// thing — .github/workflows/ios.yml, on a macOS runner — so for anyone without a
// Mac a scan like this is the ONLY pre-CI signal about the Swift half.
//
// NON-VACUITY IS ASSERTED, NOT ASSUMED. Every check below is "a pattern is
// present", and a scan whose subject MOVED reports the same nothing as a scan
// whose subject regressed. So the file must exist, the construction site must be
// found before anything is asserted about it, and a failure to find it is a
// failure — never a quiet pass over an empty match.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DeepLinkSeedDriftTests
{
    /// <summary>The pin's subject — the iOS host that boots the shell.</summary>
    private const string HostViewController = "src/BlazorNative.Apple/BnHost/HostViewController.swift";

    /// <summary>THE #282 PIN. The bridge is constructed with the cold-launch route,
    /// and the route comes from the deep-link stash.
    ///
    /// <para>Four assertions, in dependency order, because each is only meaningful if
    /// the one before it held: the file is there, the runtime is still constructed
    /// there (the anchor — if this class stopped booting the shell, the pin's subject
    /// has moved and a reader must be told rather than reassured), the bridge carries
    /// an <c>initialRoute:</c> argument, and that argument derives from
    /// <c>BnDeepLink</c> rather than being a literal.</para>
    ///
    /// <para>The fourth is not belt-and-braces. <c>AppleShellBridge(initialRoute: "/")</c>
    /// would satisfy the third check exactly while reinstating #282 in full — the
    /// argument being PRESENT is not the invariant; the argument being THE LINK is.</para></summary>
    [Fact]
    public void TheColdLaunchRouteSeed_IsStillWiredIntoTheBridge()
    {
        string path = Path.Combine(RepoRoot(), HostViewController.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path),
            $"{HostViewController} not found. This pin scans a checkout path; if the Apple shell moved, "
            + "move this constant with it — a scan that cannot find its subject must fail, not pass.");

        // COMMENTS STRIPPED. HostViewController's own justification for the fix names
        // `initialRoute` four times, and a scanner that cannot tell prose from a call
        // would pass on the DOCUMENTATION of a fix that had been reverted.
        string code = string.Join("\n", CodeLines(path).Select(l => l.Text));

        // THE ANCHOR, and it is the vacuity guard: everything below asserts that a
        // pattern is PRESENT, which is also what a scan of the wrong file reports.
        Assert.True(code.Contains("BnRuntime(", StringComparison.Ordinal),
            $"{HostViewController} no longer constructs a BnRuntime, so this pin is looking for the "
            + "cold-launch route seed in a file that no longer performs the boot. The seed itself may "
            + "be fine — but this test can no longer see it, and a pin that cannot see its subject must "
            + "never pass. Re-point it at whatever boots the shell now.");

        Match seed = Regex.Match(code, @"AppleShellBridge\s*\(\s*initialRoute\s*:\s*([^)]+)\)");
        Assert.True(seed.Success,
            "THE iOS COLD-LAUNCH ROUTE SEED IS GONE (#282). " + HostViewController + " constructs the "
            + "shell bridge without an `initialRoute:` argument, so it takes AppleShellBridge's default "
            + "of \"/\" — and nothing else on the boot path writes the route. A cold `blazornative://` "
            + "deep link would once again leave .NET's router believing it is at \"/\": an unmapped "
            + "route discarded outright, and a MAPPED one mounting the right page while "
            + "GetCurrentRouteAsync answers \"/\". Android has always seeded it unconditionally "
            + "(MainActivity: `initialRoute = deepLinkRoute ?: \"/\"`). Which routes exist is .NET's "
            + "question, and the shell must not answer it by discarding the link. Restore:\n\n"
            + "    let runtime = BnRuntime(mapper: mapper, bridge: AppleShellBridge(initialRoute: launchRoute))");

        string argument = seed.Groups[1].Value.Trim();
        bool derivesFromTheLink =
            argument.Contains("BnDeepLink", StringComparison.Ordinal)
            || Regex.IsMatch(code, $@"\b(?:let|var)\s+{Regex.Escape(argument)}\s*=[^\n]*BnDeepLink\.shared\.pendingLaunchRoute");

        Assert.True(derivesFromTheLink,
            $"the bridge is constructed with `initialRoute: {argument}`, but that argument does not "
            + "derive from BnDeepLink's cold-launch stash. A seed that is PRESENT but not fed by the "
            + "link reinstates #282 while satisfying every other check here — `initialRoute: \"/\"` is "
            + "the exact shape this refuses. The argument must be, or must be bound from, "
            + "`BnDeepLink.shared.pendingLaunchRoute…`.");
    }

    // ── The scan (the NSLogDriftTests helpers, same shapes) ──────────────────

    /// <summary>The file's lines that are CODE. Line and block comments are dropped,
    /// for the reason given at the call site. Same shape as
    /// <c>NSLogDriftTests.CodeLines</c>; <c>///</c> is covered by the <c>//</c> rule.</summary>
    private static IEnumerable<(int Number, string Text)> CodeLines(string file)
    {
        bool inBlockComment = false;
        int number = 0;

        foreach (string raw in File.ReadLines(file))
        {
            number++;
            string line = raw;

            if (inBlockComment)
            {
                int close = line.IndexOf("*/", StringComparison.Ordinal);
                if (close < 0) continue;
                inBlockComment = false;
                line = line[(close + 2)..];
            }

            int open = line.IndexOf("/*", StringComparison.Ordinal);
            if (open >= 0)
            {
                inBlockComment = line.IndexOf("*/", open, StringComparison.Ordinal) < 0;
                line = line[..open];
            }

            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0) line = line[..slashes];

            if (line.Trim().Length == 0) continue;
            yield return (number, line);
        }
    }

    /// <summary>The repo root — the nearest ancestor of the test binary holding
    /// BlazorNative.sln. The Swift sources are not a build input of this project,
    /// which is what makes <c>build-test</c> the one lane that can host this pin.
    /// Same walk as <c>NSLogDriftTests</c> and <c>GeneratedSymbolShadowTests</c>.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
