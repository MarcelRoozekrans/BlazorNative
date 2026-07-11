# BlazorNative — GitHub Project Setup Guide

This document explains how the GitHub project structure (labels, milestones,
issues, branch protection) is created and maintained — ready for community
contributions. Refreshed for the post-3.0e NativeAOT architecture at Phase 4.0.

---

## Prerequisites

**GitHub CLI** — install and authenticate:
```powershell
# Windows
winget install GitHub.cli
gh auth login
```

```bash
# macOS / Linux
brew install gh
gh auth login
```

---

## One-command setup

From the repo root, after pushing to GitHub:

```bash
bash scripts/create-github-issues.sh
```

This creates:
- **37 labels** (phase, type, contributor difficulty)
- **7 milestones** — M1–M3 created **closed** (their descriptions link the
  final-audit docs in `docs/plans/`), M4–M7 open with the ROADMAP one-liners
- **25 open-work issues** (see issue scope below)

---

## Flags

```bash
# Preview what would be created without actually creating anything
# (needs no remote, no auth — prints the full inventory and exits 0)
bash scripts/create-github-issues.sh --dry-run

# Only create labels and milestones, skip issues
bash scripts/create-github-issues.sh --labels-only

# Override the target repo (default: MarcelRoozekrans/BlazorNative)
bash scripts/create-github-issues.sh --repo=OWNER/REPO
```

---

## Label system

### Phase labels

Phase labels map to milestones (see `docs/planning/ROADMAP.md`):

| Label | Colour | Milestone |
|---|---|---|
| `phase/p0` | 🔴 Red | M1 — Runtime boots end-to-end *(complete)* |
| `phase/p1` | 🟠 Orange | M2 — First end-to-end demo on Android *(complete)* |
| `phase/p2` | 🟡 Amber | M3 — Real apps can be built *(complete)* |
| `phase/p3` | 🟡 Light | M4 — Production-shippable |
| `phase/p4` | 🟡 Pale | M5 — Full platform coverage |
| `phase/p5` | 🟢 Light green | M6 — Developer ecosystem |
| `phase/p6` | 🔵 Light blue | M7 — Framework hardening |
| `phase/p7` | 🟣 Lavender | M8 — Enterprise readiness |
| `phase/future` | ⚪ Grey | Long-term vision |

### Type labels
`type/core` · `type/android` · `type/ios` · `type/renderer` · `type/components` · `type/styling` · `type/navigation` · `type/state` · `type/http` · `type/analyzer` · `type/tooling` · `type/testing` · `type/ci` · `type/docs` · `type/security` · `type/a11y` · `type/i18n` · `type/perf` · `type/memory` · `type/ota` · `type/compliance` · `type/nativeaot` · `type/nuget`

(`type/wit` was retired with the 3.0e architecture collapse; `type/nativeaot`
covers the NativeAOT publish pipeline and C-ABI export surface.)

### Contributor labels
| Label | Meaning |
|---|---|
| `good-first-issue` | Well-scoped, good for newcomers |
| `help-wanted` | Extra attention needed from community |
| `expert-needed` | Requires deep expertise (NativeAOT, Kotlin/JNA, Blazor internals) |
| `spike` | Research / investigation required before implementation |
| `blocked` | Blocked on another issue |

---

## Issue scope: open work only

The script creates issues for **open work only** — no retro-issues for
completed M1–M3 work (those milestones are created closed, and their
descriptions link the audit record):

- **Remaining M4 phases** (4.1–4.5) — one issue each, `phase/p3`, milestone M4,
  body carries the ROADMAP phase line and links the MILESTONE.md DoD.
- **M4 DoD #4 runtime-hardening ledger** — one issue per ledger item
  (`type/core` / `type/perf`): async-handler capture window, dispatch-lane
  starvation, focus/blur wiring, stale-watcher re-attach, RemoveComponent
  bucket scan, TranslateToViewIndex memoization, RouteChanged subscriber
  isolation, allocation-budget test. Phase 4.2 triages each into
  fixed-with-tests or re-ledgered-with-rationale.
- **M5/M6 headline items** — one issue per headline item from the ROADMAP
  one-liners (`phase/p4` / `phase/p5` respectively).

M7/M8 work stays in `docs/BACKLOG.md` until those milestones approach —
this keeps the tracker focused rather than showing 170 open issues on day one.
Community members can request issues be opened for specific backlog items they
want to work on.

---

## GitHub Projects board (manual setup)

After running the script:

1. Go to the repo owner's profile → **Projects** → **New project**
2. Choose **Board** view
3. Name it `BlazorNative Roadmap`
4. Add columns: `Backlog` · `Up Next` · `In Progress` · `Done`
5. Filter by milestone to see each phase separately
6. Pin the board to the repo

---

## Recommended repo settings

### Branch protection (main)

Applied at Phase 4.0 Gate 4 via `gh api`:

- Require PR before merging (no direct pushes to `main`, admins included)
- Required status check: **`ci / build-test`** (the single Windows job from
  `.github/workflows/ci.yml` — build + analyzers, .NET test suite, the three
  NativeAOT publishes with eight-export verification, JVM `testDebugUnitTest`)
- Require conversation resolution before merging
- No force pushes

The instrumented-emulator workflow (`android-instrumented.yml`, nightly +
manual dispatch) is **informational, not a required check** — emulator-on-CI
has known flake modes; it stays advisory until a stability baseline exists.

### PR-merge workflow (from Phase 4.1 onward)

Once branch protection is live, phases merge **via PR** instead of a local
`merge --no-ff`:

1. Work happens on a phase branch (`phase-N.N-description`), same as before.
2. `gh pr create` targeting `main`; the phase's review checkpoints happen
   against the PR.
3. CI (`ci / build-test`) must be green and conversations resolved.
4. Merge on GitHub (`gh pr merge --merge` — keep the merge commit; the
   per-phase merge points remain the project's history spine).

Same review rhythm as M1–M3 — the merge just happens on GitHub with CI as the
gate.

### Issue templates

Live in `.github/ISSUE_TEMPLATE/`:
- `bug_report.md` — for bugs
- `feature_request.md` — for new features outside the roadmap
- `platform_api.md` — for new platform API proposals (camera, GPS, etc.),
  framed around the host-registered C-ABI bridge

### PR template

Lives at `.github/pull_request_template.md`. The platform checklist matches the
two surfaces every change must hold on:

```markdown
## Platform tested
- [ ] JVM dev loop (`testDebugUnitTest`)
- [ ] Android emulator (`connectedAndroidTest`)
```

(iOS rows return when the Swift shell lands in M5.)

---

## Attracting contributors

Once the repo is public with the issue structure:

1. **Write a good README** — the current `README.md` reflects the NativeAOT
   architecture; keep the badge and status banner current
2. **Add to awesome lists:**
   - [awesome-blazor](https://github.com/AdrienTorris/awesome-blazor)
   - [awesome-dotnet](https://github.com/quozd/awesome-dotnet)
3. **Post on:**
   - r/dotnet, r/Blazor, r/androiddev
   - Hacker News (Show HN — the two-page native demo app is the hook)
   - .NET Foundation Discord
   - Blazor Discord
4. **Tag issues with `good-first-issue`** — GitHub surfaces these to new contributors automatically
5. **Respond fast** — first-time contributors drop off if they don't hear back within 48 hours
