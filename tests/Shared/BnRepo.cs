namespace BlazorNative.Tests.Shared;

/// <summary>
/// THE single way a test reaches the repository tree.
///
/// WHY THIS EXISTS, and why it is not merely cleanup: before phase 15.0 this
/// walk was copy-pasted into 24 separate test files -- 23 named `RepoRoot`, and
/// one inlined into `BnImageDemoTests.ShellSource`, which is why counting by
/// method name undercounted it. They had not drifted: all 24 used the same
/// `BlazorNative.sln` sentinel and the same walk. But 24 copies of one truth is
/// the precondition for the twin-divergence class that
/// milestone 14 spent five phases closing. #339 was copies-agreeing right up
/// until one was not.
///
/// It also does a second job the copies could not. The pin population is NOT
/// enumerable by name -- `Drift`, `Pin`, `Sweep` and `Roster` are all in use, so
/// a convention test keyed on a suffix would miss a quarter of it. Callers of
/// THIS method are the population, exactly. PinPopulationTests depends on that,
/// so a test that reaches the tree another way is invisible to enforcement --
/// which is what that test exists to catch.
/// </summary>
internal static class BnRepo
{
    /// <summary>Walks up from the test binary to the directory holding
    /// BlazorNative.sln. Fails loudly when it cannot find it: a pin that cannot
    /// locate its subject must red, never pass over nothing.</summary>
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException(
                "could not find BlazorNative.sln above the test binary — a pin that cannot find "
                + "its subject must fail loudly, never vacuously");

        return dir.FullName;
    }
}
