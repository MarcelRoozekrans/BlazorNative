# Contributing to BlazorNative

Thanks for your interest in BlazorNative. This guide covers how to set up, build,
test, and land a change — and the one governance step that must happen **before**
your first contribution can be merged: signing the CLA.

> **You must sign the Contributor License Agreement before a maintainer can merge
> your first pull request.** See [Contributor License Agreement](#contributor-license-agreement)
> below. This does not change the license consumers of the framework receive — the
> framework core stays **MIT** — it only preserves the maintainer's ability to offer
> a dual-licensed / commercial edition later. It costs you nothing and keeps the
> project's options open.

---

## Ways to contribute

- **Report a bug or gap.** Open an issue with the smallest reproduction you can, the
  observed vs. expected behavior, and — for on-device reports — the platform, OS
  version, and architecture. The best bug reports in this repo pair a claim with a
  *measurement* (a captured log line, a count, a frame), and unpaired "nothing
  happened" is treated with suspicion (see issue #191 for why).
- **Propose a change.** For anything beyond a small fix, open an issue first so the
  approach can be agreed before you write code — this repo has strong, deliberate
  conventions (a frozen C ABI, per-package public-API baselines, a zero-warning bar)
  and a design conversation up front saves a rewrite.
- **Send a pull request.** See [Pull requests](#pull-requests).

---

## Prerequisites

BlazorNative is a Blazor app compiled with NativeAOT into an Android (Kotlin/JNA)
shell and an iOS-simulator (Swift) shell. A full build needs:

| Tool | For |
|---|---|
| **.NET 10 SDK** (the exact band is pinned in [`global.json`](global.json)) | build + NativeAOT publish + the .NET test suite |
| **JDK 21** | the JVM/JNA dev loop and the Android shell |
| **Android SDK + NDK** (NDK 26.3) | the Android shell + instrumented tests |
| **Xcode** (macOS only) | the iOS-simulator shell + XCTest lane |
| **Node.js** | the docs site (`website/`) |

On Windows, [`setup.ps1`](setup.ps1) installs and verifies the .NET / JDK / Android
prerequisites for you. You do **not** need all four toolchains to make a useful
change — a .NET-only change only needs the .NET SDK; a Kotlin change needs the JVM
side; and the iOS lane requires macOS.

---

## Build and test

The required PR gate is the `.NET` suite plus the JVM dev loop — both must be green,
and both are **count-pinned** (see [Test counts](#test-counts-are-pinned)).

```sh
# .NET — the core suite (the count is asserted in CI)
dotnet build BlazorNative.sln -c Release
dotnet test  BlazorNative.sln -c Release

# JVM dev loop (loads the published win-x64 native lib via JNA)
cd src/BlazorNative.Jni && ./gradlew testDebugUnitTest

# Android instrumented (needs an emulator/AVD — advisory lane)
./gradlew connectedAndroidTest

# iOS simulator (macOS only — advisory lane)
xcodebuild test   # see the `ios` lane in .github/workflows/ios.yml

# Docs site (generates the API reference, then builds the site)
cd website && npm install && npm run build
```

The four surfaces and where each is asserted are tabulated in the README's **Test
surface** section. Only `build-test` (.NET + JVM) gates a PR; Android and iOS are
advisory lanes (nightly / on-merge / manual dispatch).

---

## Project conventions

These are not stylistic preferences — most are enforced in CI and will red your PR
if you skip them.

### Conventional Commits (this drives releases)

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).
Releases are cut automatically by **release-please** from the commit history, so the
*type* you choose sets the next version:

| Prefix | Meaning | Version effect |
|---|---|---|
| `fix:` | a bug fix | patch (0.0.**x**) |
| `feat:` | a new capability | minor (0.**x**.0) |
| `feat!:` / `BREAKING CHANGE:` | breaking change | major (once ≥ 1.0) |
| `docs:` `chore:` `test:` `ci:` `refactor:` | no user-facing code change | no release on its own |

Keep the subject imperative and lowercase-led (the `pr-title` check enforces
commitlint's subject-case — e.g. `feat: add …`, not `feat: Add …`). Explain the
**why** in the body; this codebase values a comment or message that says what a
future reader can't reconstruct from the diff.

### Branch naming

Short, prefixed, issue-scoped: `feat/<n>-<slug>`, `fix/<n>-<slug>`,
`docs/<n>-<slug>`, `chore/<slug>`. Branch off `main`; PRs are **squash-merged**.

### The zero-warning bar

The pack that ships the packages (`scripts/consumer-smoke.ps1`, run in `build-test`
and again at release) fails on **any** compiler warning. A warning is not a nag
here — it fails the build. If you touch a public API's XML docs, note that an
unresolved `<see cref>` is a `CS1574` warning and will red the pack.

### Test counts are pinned

The .NET and JVM suites assert an **exact** pass count (see `.github/workflows/ci.yml`
and the README's Test surface table). If your change adds or removes tests, update
those counts **in the same PR**, with a one-line ledger note explaining the delta —
CI will red otherwise, on purpose: a silent count move is how a dropped test hides.

### Public API + the API-tier baselines

Every shipped package carries a `PublicAPI.Shipped.txt` baseline; `RS0016`/`RS0017`/
`RS0037` are **errors**. If you change a package's public surface you must update its
baseline in the same PR. Types that are public only for the C ABI / AOT export are
marked `[EditorBrowsable(Never)]` (the "NOT-API" tier) and are guarded by
`NotApiEditorBrowsableTests` — don't add public surface without deciding its tier.

### The wire vocabulary is generated — edit the manifest, not the tables

Style names, the scroll-node ignore list and the node-type vocabulary live **once**,
in [`src/wire-vocabulary.json`](src/wire-vocabulary.json). `tools/BlazorNative.WireGen`
emits every copy — C#, Kotlin (repo **and** template), Objective-C++ and Swift — into
files named `BnWireVocabulary.g.*`:

```sh
dotnet run --project tools/BlazorNative.WireGen            # regenerate
dotnet run --project tools/BlazorNative.WireGen -- --check # is anything stale?
```

Never hand-edit a `*.g.*` file. `WireVocabularyCodegenTests` re-runs the emitters
in-process and byte-compares against what is committed, so a hand-edit — or a manifest
change without regenerating — fails the required `build-test` lane.

**Adding a style name is two jobs, and the generator only does the first.** It updates
every routing *table*; it cannot write the *setter* at the other end. A routed name with
no implementation is silently dropped at runtime on that platform, which is the exact
failure this apparatus exists to prevent — so
`AndroidSetStyleDispatch_HasAnArmForEveryYogaStyle` (Kotlin, source-level) and
`BnYogaStyleParserTests.testEveryRoutedNameReachesASetter` (iOS, runtime) will red until
you implement it.

### Documentation

The API reference under `website/docs/reference/` is **generated** from each package's
XML docs (`scripts/generate-reference.ps1`) and is never committed. If you add or
change a public member, write its `///` doc — packages with `BnEnforceDocCoverage`
on treat a missing doc as a build error (`CS1591`). Enum members are referenced with
`<c>…</c>`, not `<see cref>` (they render as a table, not an anchor).

---

## Pull requests

1. **Open (or claim) an issue** for anything non-trivial, and agree the approach.
2. **Sign the CLA** — see below. Your first PR can't merge until this is recorded.
3. **Branch, implement, and keep it focused** — one logical change per PR.
4. **Green the gate** — `build-test` (.NET + JVM) must pass; update any pinned counts
   and PublicAPI baselines your change moves.
5. **Fill in the PR template** — link the issue, say what changed and how you tested
   it, and check the platforms you exercised.
6. A maintainer reviews and squash-merges. Releases and the nuget publish are
   maintainer-run.

---

## Contributor License Agreement

Before your first contribution is merged, you (or, for work done for an employer,
your company) must agree to the **Contributor License Agreement** in [`CLA.md`](CLA.md).

**Why a CLA and not just a DCO:** a `Signed-off-by` DCO only certifies you had the
right to submit under the existing license — it grants the project no additional
rights. The CLA grants the maintainer a broad license over your contribution, which
is what preserves the ability to offer a dual-licensed / commercial edition later
**without** changing the permissive MIT license everyone else receives. The CLA is
about retaining the maintainer's options, not restricting what consumers can do.

**How to sign:** the intended mechanism is a CLA-assistant check that posts on your
first PR and records your agreement (a maintainer enables this on the repository). If
that check is not yet active when you open your PR, a maintainer will tell you how to
submit a signed copy. Either way, agreement is recorded once and covers your future
contributions.

---

## Code of conduct

Be respectful, assume good faith, and keep discussion technical. Harassment or
abusive behavior isn't welcome here. Report concerns to the maintainer via the email
in the package metadata.

---

## License

The framework is released to consumers under the **MIT License** (see [`LICENSE`](LICENSE)),
and that does not change. Your contributions are made under the CLA above, which
grants the maintainer a license over your work while leaving your own copyright
intact.
