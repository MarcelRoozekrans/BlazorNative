# AGENTS.md: BlazorNative

BlazorNative lets an app author write Blazor components that are compiled with NativeAOT into one
native library per platform and rendered as real Android and iOS views, with Yoga doing layout. The
repo ships seven NuGet packages from `src/` (Core, Renderer, Http, Components, Device, Testing,
Runtime) plus the Roslyn analyzers package, the `dotnet new blazornative` template, a Docusaurus
site and the Kotlin and Swift shells. It is pre-1.0; the C ABI (80-byte struct, 10 exports) is frozen.

## Layout

- `src/` holds exactly the shipped packages. `src/Directory.Build.props` is their only metadata home
  and does not apply to `tests/`, `samples/` or `tools/`. `src/Directory.Build.targets` turns
  `CS1591` into an error for the packages that set `BnEnforceDocCoverage`. Do not move that
  condition into the `.props`: it is evaluated before the project body and silently does nothing.
- `samples/BlazorNative.SampleApp` is the NativeAOT publish head, not `src/BlazorNative.Runtime`.
  `samples/ConsumerSmoke` is a blank consumer used by the pack smoke.
- Generated, never hand-edited: every `*.g.*` file. Their sources are `src/wire-vocabulary.json`
  and `src/deeplink-vectors.json`; regenerate with `dotnet run --project tools/BlazorNative.WireGen`.
  The Kotlin and Swift copies and the template's Kotlin copy are generated too.
- `website/docs/reference/` is generated from XML docs and never committed.
- `docs/planning/` is state kept by planning tooling, not instructions.

## Build and test

Prerequisites: `global.json` pins SDK `11.0.100` with `rollForward: latestPatch`. The template payload
`templates/BlazorNative.Templates/content/BlazorNative.App/global.json` carries the same pin and is a
second place to change if the pin moves. The packages and the three test projects target
`net10.0;net11.0`, so both the .NET 10 and .NET 11 runtimes must be installed to run the tests. The
tools and `SampleApp` target `net10.0`. `pwsh` is needed by the reference-drift tests. CI runs
`build-test` on `windows-latest`; the commands below also pass on Linux.

CI job `build-test` in `.github/workflows/ci.yml`:

```sh
dotnet build BlazorNative.sln -c Release -tl:off -nologo
dotnet test BlazorNative.sln -c Release --no-build --nologo -v minimal --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
dotnet run --project tools/BlazorNative.DocSamples -c Release -- --out artifacts/doc-samples
```

- The test step also asserts the summed `Passed` count over all projects and both frameworks: 2828
  passed, 0 skipped (Analyzers 27, Renderer 147, Runtime 1240, each twice). Adding or removing a test
  means updating that number in `ci.yml` in the same PR. The JVM suite is pinned the same way, at 210.
- The same job publishes `samples/BlazorNative.SampleApp` for `win-x64`, `linux-bionic-x64` and
  `linux-bionic-arm64` and asserts exactly four `IL2072` warnings and no other IL warning. It needs
  the pinned NDK (`NDK_PIN` in `ci.yml`), so it is not part of the inner loop.
- The `coverage` job gates and uploads the Cobertura reports through ci-baseline; the commands above
  do not reproduce that gate.
- `baseline-checks.yml` runs the shared gitleaks, CodeQL and NuGetAudit baseline.

Inner loop, not run by a CI job as written:

```sh
dotnet test tests/BlazorNative.Renderer.Tests -c Release --no-build --nologo -f net10.0
dotnet run --project tools/BlazorNative.WireGen -- --check
```

## Conventions and gotchas

- One version, eight literals in five files, all rewritten by release-please from lines annotated
  `x-release-please-version`: `src/Directory.Build.props`, `src/BlazorNative.Runtime/Exports.cs`,
  `templates/BlazorNative.Templates/BlazorNative.Templates.csproj`, the template's
  `MyBlazorNativeApp.csproj` and its `.template.config/template.json`. Never edit one by hand, and
  never write that annotation token in prose in those files: the updater matches it anywhere on a line.
- Yoga has four version pins that must agree, asserted by the first step of `build-test`: the
  `build.gradle.kts` under `src/BlazorNative.Jni`, `YOGA_VERSION` in `ci.yml`, `YOGA_VERSION` in
  `.github/workflows/ios.yml`, and the template's `android/build.gradle.kts`. Bump all four together.
- Seven packages carry a `PublicAPI.Shipped.txt`, and `RS0016`, `RS0017` and `RS0037` are errors.
  A public surface change updates the baseline in the same PR. Types public only for the C ABI are
  `[EditorBrowsable(Never)]`.
- A new public member in a `BnEnforceDocCoverage` package needs a `///` doc. Enum members are
  referenced with `<c>`, not `<see cref>`. An unresolved `<see cref>` is `CS1574` and fails the pack.
- The pack smoke fails on any compiler warning. The build log itself is not warning-free, so check
  that your change adds none rather than expecting a clean log.
- Adding a style name needs the generated tables and also a setter on each platform; the Kotlin and
  iOS dispatch tests fail until it exists.
- Many invariants are drift pins: tests that read the repo tree and compare two copies. A new pin
  follows `docs/pin-standard.md`: it must fail when it scans nothing, prove its detector detects, and
  reach the checkout through `BnRepo.Root()`.
- Narrative docs under `website/docs` state no version and no hand-kept count.
- Android and iOS lanes (`android-instrumented.yml`, `ios.yml`) are advisory and need an emulator or
  macOS. Their commands are left out here because they cannot be run on a Linux agent.

<!-- org-core:start sha256=08a88996bc9165f888fc0260607cfd0670cae128c323e16707c6b452ee66222d (generated from docker-nuc scripts/agents-md/org-core-personal.md; do not edit here) -->
## Org rules

These rules hold in every public MarcelRoozekrans repo.

### Commits and releases

- Conventional Commits. release-please reads them on the default branch, and the changelog sections
  in `release-please-config.json` decide which types count. The standard: `feat`, `fix`, `perf`,
  `deps` and `revert` open or update the release PR; `docs`, `test`, `refactor`, `build`, `ci`,
  `chore` and `style` do not, unless the commit is breaking or has a `Release-As:` footer. The
  release happens when that PR merges. A breaking change needs `!` or a `BREAKING CHANGE:` footer.
- A commit body never contains nested parentheses. release-please cannot parse them and silently
  drops the whole commit from the changelog, while the workflow still reports success.
- After merging a commit that should release, check that it appears in the release-please PR.

### Branches and pull requests

- Every change goes through a branch and a pull request. Never push to the default branch directly.
- A PR body says what the diff does, and nothing the diff does not do.
- Fix review findings in the PR under review, including small ones. File an issue only for a finding
  that belongs to other code, needs a design decision, is its own piece of work, or waits on
  something external.

### CI

- The .NET repos call the shared workflows of `MarcelRoozekrans/ci-baseline`, pinned to `@v1`, never
  `@main`.
- Its coverage gate fails the build when line coverage drops below the floor of 80%, and Codecov
  fails a PR that lowers project coverage by more than 1 percentage point. New code comes with
  tests.
- A repo with a lower floor documents the reason and links the issue that raises it.
<!-- org-core:end -->
