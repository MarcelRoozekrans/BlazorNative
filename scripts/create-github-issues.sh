#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# BlazorNative — GitHub Project Setup Script (post-3.0e, NativeAOT era)
#
# Creates labels, milestones, and OPEN-WORK issues on GitHub:
#   • Labels: phase / type / contributor taxonomy
#   • Milestones: M1–M3 created CLOSED (descriptions link their final-audit
#     docs), M4–M7 created open with the ROADMAP one-liners
#   • Issues: open work only — remaining M4 phases (4.1–4.5), the M4 DoD #4
#     runtime-hardening ledger items, and M5/M6 headline items.
#     No retro-issues for completed M1–M3 work.
#
# Run this ONCE after pushing the repo to GitHub (Phase 4.0 Gate 4).
#
# ⚠ Already executed 2026-07-11 (Phase 4.0). The label taxonomy below is kept
#   current with the roadmap (post-2026-07-13 re-plan numbering), but the
#   MILESTONE and ISSUE sections still describe the Phase 4.0 bootstrap state
#   (M4–M7 open, pre-re-plan names) — do NOT re-run them against the live
#   repo without updating them first; labels-only re-runs (--labels-only)
#   are safe and idempotent (--force updates in place).
#
# Prerequisites:
#   • GitHub CLI installed: https://cli.github.com
#   • Authenticated: gh auth login
#   • Run from the repo root: bash scripts/create-github-issues.sh
#
# Flags:
#   --dry-run          Print what would be created without creating anything
#                      (no repo/remote/auth needed)
#   --labels-only      Only create labels and milestones, skip issues
#   --repo=OWNER/REPO  Override repo (default: MarcelRoozekrans/BlazorNative)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

DRY_RUN=false
LABELS_ONLY=false
REPO="MarcelRoozekrans/BlazorNative"

for arg in "$@"; do
  case $arg in
    --dry-run)     DRY_RUN=true ;;
    --labels-only) LABELS_ONLY=true ;;
    --repo=*)      REPO="${arg#*=}" ;;
  esac
done

# ── Helpers ───────────────────────────────────────────────────────────────────

GREEN='\033[0;32m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
YELLOW='\033[0;33m'
NC='\033[0m'

log()  { echo -e "${CYAN}  ⟶  $1${NC}"; }
ok()   { echo -e "${GREEN}  ✓  $1${NC}"; }
skip() { echo -e "${GRAY}  ○  $1${NC}"; }
warn() { echo -e "${YELLOW}  ⚠  $1${NC}"; }

# Runs gh with stderr VISIBLE and returns gh's real exit status —
# callers count failures per category and the script exits non-zero if any.
gh_cmd() {
  if [ "$DRY_RUN" = true ]; then
    # Compact one-line echo; long issue bodies are summarised by the callers.
    # Written to stderr so caller stdout redirects can't hide the preview.
    local preview="gh $*"
    echo -e "${GRAY}  [dry-run] ${preview:0:110}…${NC}" >&2
    return 0
  fi
  gh "$@"
}

# Inventory counters (printed at the end — the dry-run verification target)
LABEL_COUNT=0
MILESTONE_OPEN_COUNT=0
MILESTONE_CLOSED_COUNT=0
ISSUE_COUNT=0

# Failure counters (real runs only — any non-zero total fails the script)
LABEL_FAIL=0
MILESTONE_FAIL=0
ISSUE_FAIL=0

# Prints the failure summary and exits 1 if anything failed; no-op otherwise.
finish_or_fail() {
  local total=$((LABEL_FAIL + MILESTONE_FAIL + ISSUE_FAIL))
  if [ "$total" -gt 0 ]; then
    echo ""
    warn "FAILURES: $LABEL_FAIL label(s), $MILESTONE_FAIL milestone(s), $ISSUE_FAIL issue(s) — see stderr above"
    warn "The inventory above counts successful creations only."
    exit 1
  fi
}

echo ""
echo "  BlazorNative — GitHub Project Setup"
echo "  Repo: $REPO"
echo "  Dry run: $DRY_RUN"
echo ""

# ─────────────────────────────────────────────────────────────────────────────
# 1. Labels
# ─────────────────────────────────────────────────────────────────────────────

echo "  ── Labels ──────────────────────────────────────────────"

create_label() {
  local name=$1 color=$2 desc=$3
  log "Label: $name"
  if gh_cmd label create "$name" \
    --repo "$REPO" \
    --color "$color" \
    --description "$desc" \
    --force; then  # --force updates if already exists
    LABEL_COUNT=$((LABEL_COUNT + 1))
  else
    warn "FAILED label: $name"
    LABEL_FAIL=$((LABEL_FAIL + 1))
  fi
}

# Phase labels (mapped to milestones — see docs/planning/ROADMAP.md;
# descriptions follow the 2026-07-13 re-plan: M6 repositioned as Real-UI
# Foundation, the old P5/P6 pushed to M8/M10)
create_label "phase/p0"      "B60205" "M1 — Runtime boots end-to-end (complete)"
create_label "phase/p1"      "D93F0B" "M2 — First end-to-end demo on Android (complete)"
create_label "phase/p2"      "E4810A" "M3 — Real apps can be built (complete)"
create_label "phase/p3"      "F9D0C4" "M4 — Production-shippable (complete)"
create_label "phase/p4"      "FEF2C0" "M5 — Full platform coverage (complete)"
create_label "phase/p5"      "C2E0C6" "M6 — Real-UI Foundation: layout + scroll + image (complete)"
create_label "phase/p6"      "BFD4F2" "M7 — Components + Razor"
create_label "phase/p7"      "D4C5F9" "M8 — Developer ecosystem"
create_label "phase/p8"      "C5DEF5" "M9 — Platform breadth + real device"
create_label "phase/p9"      "F7C6C7" "M10 — Framework hardening"
create_label "phase/future"  "EEEEEE" "Long-term vision"

# Type labels
create_label "type/core"       "0075CA" "Core runtime / bridge"
create_label "type/android"    "3CB371" "Android native shell (Kotlin + JNA)"
create_label "type/ios"        "8B8B8B" "iOS native shell (Swift — M5)"
create_label "type/renderer"   "6A4C93" "Headless renderer / patch protocol"
create_label "type/components" "E8A838" "Blazor component library (Bn*)"
create_label "type/styling"    "F7C6C7" "Styling system"
create_label "type/navigation" "C5DEF5" "Navigation system"
create_label "type/state"      "BFD4F2" "State management"
create_label "type/http"       "0E8A16" "HTTP / networking"
create_label "type/analyzer"   "5319E7" "Roslyn analyzers"
create_label "type/tooling"    "1D76DB" "CLI / build tooling"
create_label "type/testing"    "006B75" "Test infrastructure"
create_label "type/ci"         "159818" "CI/CD pipelines"
create_label "type/docs"       "FBCA04" "Documentation"
create_label "type/security"   "B60205" "Security"
create_label "type/a11y"       "7057FF" "Accessibility"
create_label "type/i18n"       "E4E669" "Internationalisation"
create_label "type/perf"       "FF7619" "Performance"
create_label "type/memory"     "C7DEF8" "Memory management"
create_label "type/ota"        "F9D0C4" "Over-the-air updates"
create_label "type/compliance" "EEEEEE" "Legal / compliance"
create_label "type/nativeaot"  "0075CA" "NativeAOT publish / C-ABI export surface"
create_label "type/nuget"      "5319E7" "NuGet packaging"

# Contributor labels
create_label "good-first-issue" "7057FF" "Good for newcomers"
create_label "help-wanted"      "008672" "Extra attention needed"
create_label "expert-needed"    "B60205" "Requires deep expertise (NativeAOT, Kotlin/JNA, Blazor internals)"
create_label "spike"            "FBCA04" "Research / investigation required"
create_label "blocked"          "E4E669" "Blocked on another issue"

ok "Labels created"

# ─────────────────────────────────────────────────────────────────────────────
# 2. Milestones
# ─────────────────────────────────────────────────────────────────────────────

echo ""
echo "  ── Milestones ───────────────────────────────────────────"

create_milestone() {
  local title=$1 state=$2 desc=$3
  log "Milestone ($state): $title"
  if gh_cmd api \
    --method POST \
    -H "Accept: application/vnd.github+json" \
    "repos/$REPO/milestones" \
    -f title="$title" \
    -f description="$desc" \
    -f state="$state" > /dev/null; then
    if [ "$state" = "closed" ]; then
      MILESTONE_CLOSED_COUNT=$((MILESTONE_CLOSED_COUNT + 1))
    else
      MILESTONE_OPEN_COUNT=$((MILESTONE_OPEN_COUNT + 1))
    fi
  else
    warn "FAILED milestone: $title"
    MILESTONE_FAIL=$((MILESTONE_FAIL + 1))
  fi
}

# Completed milestones — created closed; descriptions link the final-audit docs.
create_milestone "M1 — P0: Runtime Boots End-to-End" "closed" \
"Complete 2026-05-24, tagged v1.0. Toolchain, renderer internal-API strategy, cooperative scheduler, export surface. Final audit: docs/plans/2026-05-24-milestone-1-final-audit.md"

create_milestone "M2 — P1: First End-to-End Demo on Android" "closed" \
"Complete 2026-05-28, tagged v2.0. HelloComponent rendered as native Android widgets via the Kotlin shell. Final audit: docs/plans/2026-05-27-milestone-2-final-audit.md"

create_milestone "M3 — P2: Real Apps Can Be Built" "closed" \
"Complete 2026-07-10, tagged v3.0. NativeAOT re-platform (one .so per ABI, typed eight-export C-ABI), Bn* components, @bind mechanics, bidirectional events, shell bridge, navigation — a real two-page app on the AVD. Final audit: docs/plans/2026-07-10-milestone-3-final-audit.md"

# Open milestones — ROADMAP one-liners (docs/planning/ROADMAP.md).
create_milestone "M4 — P3: Production-Shippable" "open" \
"The repo goes public with CI as the safety net; the BN analyzers are re-attached and tested; the runtime-hardening ledger is triaged into fixed-or-deliberately-deferred; the dev inner loop (fast-restart, honestly not hot-reload) and NuGet packages exist for outside consumers. Windows + Android only — the iOS Swift shell is deferred to M5. Full DoD: docs/planning/MILESTONE.md"

create_milestone "M5 — P4: Full Platform Coverage" "open" \
"Android shell complete (lifecycle, permissions, FCM, secure storage, deep links, predictive back). iOS shell complete (APNs, Keychain, universal links, App Store validation). Cross-platform APIs: geolocation, camera, clipboard, share, haptics, biometrics, purchases, background tasks."

create_milestone "M6 — P5: Developer Ecosystem" "open" \
"BlazorNative.Components, BlazorNative.Styling, BlazorNative.State, BlazorNative.Navigation, BlazorNative.Cli global tool, full test infrastructure, CI/CD release pipeline, documentation site, NuGet packaging."

create_milestone "M7 — P6: Framework Hardening" "open" \
"Security model (signed native binaries, URL allowlist, secure buffers, crash isolation), error handling and crash recovery, accessibility, i18n (InvariantGlobalization workaround), performance monitoring, memory management, bridge C-ABI contract hardening."

ok "Milestones created"

if [ "$LABELS_ONLY" = true ]; then
  echo ""
  ok "Done (labels + milestones only)"
  echo ""
  echo "  ── Inventory ────────────────────────────────────────────"
  echo "  Labels:     $LABEL_COUNT"
  echo "  Milestones: $((MILESTONE_CLOSED_COUNT + MILESTONE_OPEN_COUNT)) ($MILESTONE_CLOSED_COUNT closed, $MILESTONE_OPEN_COUNT open)"
  echo "  Issues:     0 (skipped — --labels-only)"
  echo ""
  finish_or_fail
  exit 0
fi

# ─────────────────────────────────────────────────────────────────────────────
# 3. Issues — OPEN WORK ONLY
#
# No issues are created for completed M1–M3 work; those milestones are closed
# and their descriptions link the audit record.
# ─────────────────────────────────────────────────────────────────────────────

echo ""
echo "  ── Issues ───────────────────────────────────────────────"

# Helper: create a single issue
# Usage: issue MILESTONE LABELS TITLE BODY
issue() {
  local milestone=$1 labels=$2 title=$3 body=$4
  log "Issue: $title"
  if gh_cmd issue create \
    --repo "$REPO" \
    --title "$title" \
    --body "$body" \
    --label "$labels" \
    --milestone "$milestone"; then
    ISSUE_COUNT=$((ISSUE_COUNT + 1))
  else
    warn "FAILED issue: $title"
    ISSUE_FAIL=$((ISSUE_FAIL + 1))
  fi
  [ "$DRY_RUN" = true ] || sleep 1  # content-creation spacing (GitHub secondary-rate-limit guidance)
}

M4="M4 — P3: Production-Shippable"
M5="M5 — P4: Full Platform Coverage"
M6="M6 — P5: Developer Ecosystem"

DOD_LINK="Definition of Done: [docs/planning/MILESTONE.md](docs/planning/MILESTONE.md)"
LEDGER_FOOTER="Triage in Phase 4.2 per MILESTONE.md DoD #4: **fixed with tests** or **explicitly re-ledgered with a written rationale** in the triage doc."

# ── M4 — remaining phases (4.1–4.5) ──────────────────────────────────────────

issue "$M4" "phase/p3,type/analyzer" \
"Phase 4.1 — Analyzer rescope + unit tests" \
"From the ROADMAP M4 phase plan (docs/planning/ROADMAP.md):

> **Phase 4.1** — Analyzer rescope + unit tests (DoD #3)

The legacy BN rules (written for the pre-NativeAOT runtime) are retired or reframed for the NativeAOT world; the analyzer project is re-attached to the runtime project graph (detached since Phase 3.0e); every surviving BN rule gets \`Microsoft.CodeAnalysis.Testing\` coverage — fires on bad code, silent on correct code, fix verified where one exists. Release-tracking files (the RS2008 deferral from M1) land with the tests.

$DOD_LINK (DoD #3)."

issue "$M4" "phase/p3,type/core" \
"Phase 4.2 — Runtime hardening: ledger triage + fixes" \
"From the ROADMAP M4 phase plan (docs/planning/ROADMAP.md):

> **Phase 4.2** — Runtime hardening: ledger triage + fixes (DoD #4)

Every open runtime-hardening ledger item is either fixed with tests or explicitly re-ledgered with a written rationale. Each ledger item is tracked as its own issue in this milestone (see the \`type/core\` / \`type/perf\` hardening issues). Load-bearing minimum expected to be fixed rather than re-ledgered: focus/blur wiring, the RouteChanged subscriber-isolation decision, the allocation-budget test.

$DOD_LINK (DoD #4)."

issue "$M4" "phase/p3,type/tooling" \
"Phase 4.3 — Dev inner loop / fast-restart" \
"From the ROADMAP M4 phase plan (docs/planning/ROADMAP.md):

> **Phase 4.3** — Dev inner loop / fast-restart (DoD #5)

File-watcher → incremental win-x64 publish → JVM host reload as the fast lane, plus the ADB-push → app-restart story for on-device iteration. Documented honestly: NativeAOT cannot hot-patch — this is **fast-restart, not hot-reload** — and the measured round-trip times are recorded.

$DOD_LINK (DoD #5)."

issue "$M4" "phase/p3,type/tooling" \
"Phase 4.4 — DevTools render-tree inspector" \
"From the ROADMAP M4 phase plan (docs/planning/ROADMAP.md):

> **Phase 4.4** — DevTools render-tree inspector (DoD #6)

A dev-host surface showing the live patch stream, the current widget tree (collapsible), and the event log against a running session.

$DOD_LINK (DoD #6)."

issue "$M4" "phase/p3,type/nuget" \
"Phase 4.5 — NuGet packaging + consumer smoke + M4 close" \
"From the ROADMAP M4 phase plan (docs/planning/ROADMAP.md):

> **Phase 4.5** — NuGet packaging + consumer smoke + M4 final audit → \`v4.0\` (DoD #7, #8)

\`BlazorNative.Core\`, \`.Renderer\`, \`.Http\`, \`.Components\`, and \`.Analyzers\` pack cleanly (local/CI feed; nuget.org publication is a separate decision at milestone close). Proof is a consumer smoke: a blank project referencing only the packages mounts a \`Bn*\` component and produces frames. The analyzers package ships its \`.props\`/\`.targets\` correctly. Closes with the M4 final-audit doc → tag \`v4.0\`.

$DOD_LINK (DoD #7, #8)."

# ── M4 — DoD #4 runtime-hardening ledger (one issue per item) ────────────────

issue "$M4" "phase/p3,type/core" \
"Hardening ledger: async-handler exception-capture window" \
"The dispatch exception-capture window in \`NativeRenderer\` is depth-counted around each synchronous dispatch (Phase 3.2). Handlers that go async can complete outside the window, so their exceptions escape the rc-2 surface. Revisit the capture window for async handlers.

Source: [docs/plans/2026-07-09-phase-3.2-conclusion.md](docs/plans/2026-07-09-phase-3.2-conclusion.md)

$LEDGER_FOOTER"

issue "$M4" "phase/p3,type/core" \
"Hardening ledger: dispatch-lane starvation" \
"All UI events funnel through the single Kotlin dispatch lane (\`BlazorNative-Dispatch\`, Phase 3.2). A long-running or blocked handler starves every subsequent event. Watch item since 3.2 — decide whether the single-lane model needs a guard (timeout, watchdog, or documented contract).

Source: [docs/plans/2026-07-09-phase-3.2-conclusion.md](docs/plans/2026-07-09-phase-3.2-conclusion.md)

$LEDGER_FOOTER"

issue "$M4" "phase/p3,type/core" \
"Hardening ledger: focus/blur events unwired" \
"Click and change events are plumbed end-to-end (Phase 3.2); focus/blur are not wired from the Android widgets through \`blazornative_dispatch_event\` to .NET handlers. MILESTONE DoD #4 names this a load-bearing item expected to be **fixed**, not re-ledgered.

Source: [docs/plans/2026-07-09-phase-3.2-conclusion.md](docs/plans/2026-07-09-phase-3.2-conclusion.md)

$LEDGER_FOOTER"

issue "$M4" "phase/p3,type/core" \
"Hardening ledger: stale-watcher re-attach keying" \
"The \`EditText\` text-watcher re-attach path after re-render carries a stale-watcher caveat (Phase 3.3): watcher keying can leave a stale watcher attached across renders. Revisit the re-attach keying.

Source: [docs/plans/2026-07-10-phase-3.3-conclusion.md](docs/plans/2026-07-10-phase-3.3-conclusion.md)

$LEDGER_FOOTER"

issue "$M4" "phase/p3,type/perf" \
"Hardening ledger: RemoveComponent bucket scan" \
"Component removal scans the slot buckets linearly (Phase 3.3 slot-list model). Perf-ledger item: acceptable at POC scale, needs a measured decision before component counts grow.

Source: [docs/plans/2026-07-10-phase-3.3-conclusion.md](docs/plans/2026-07-10-phase-3.3-conclusion.md)

$LEDGER_FOOTER"

issue "$M4" "phase/p3,type/perf" \
"Hardening ledger: TranslateToViewIndex memoization" \
"Host insert-index translation recomputes the component-slot-chain base offset per patch (Phase 3.3). Perf-ledger item: memoize the translation or document why recompute-per-patch is fine at the measured scale.

Source: [docs/plans/2026-07-10-phase-3.3-conclusion.md](docs/plans/2026-07-10-phase-3.3-conclusion.md)

$LEDGER_FOOTER"

issue "$M4" "phase/p3,type/core" \
"Hardening ledger: RouteChanged subscriber isolation" \
"A throwing \`RouteChanged\` subscriber can disrupt sibling subscribers and the navigation notification path (Phase 3.5 honest boundary). MILESTONE DoD #4 names the subscriber-isolation decision a load-bearing item expected to be **fixed**, not re-ledgered.

Source: [docs/plans/2026-07-10-phase-3.5-conclusion.md](docs/plans/2026-07-10-phase-3.5-conclusion.md)

$LEDGER_FOOTER"

issue "$M4" "phase/p3,type/perf" \
"Hardening ledger: allocation-budget test" \
"Steady-state render path allocation-budget test, deferred from M1 Phase 1.1 with an explicit \"Milestone 4\" marker. MILESTONE DoD #4 names it a load-bearing item expected to be **fixed** (i.e., the test lands), not re-ledgered.

Source: [docs/plans/2026-07-10-milestone-3-final-audit.md](docs/plans/2026-07-10-milestone-3-final-audit.md) (carryover table)

$LEDGER_FOOTER"

# ── M5 — headline items ──────────────────────────────────────────────────────

issue "$M5" "phase/p4,type/android" \
"Android shell completeness" \
"From the ROADMAP M5 one-liner (docs/planning/ROADMAP.md): lifecycle, permissions, FCM push, secure storage, deep links, predictive back — the Kotlin shell grows from demo host to complete platform shell."

issue "$M5" "phase/p4,type/ios,expert-needed" \
"iOS shell — NativeAOT ios-arm64 static lib + Swift shell" \
"Deferred from M4 at milestone-open. The iOS shell mirrors the Kotlin one: a NativeAOT \`ios-arm64\` static lib exposing the same eight-export C-ABI, consumed by a thin Swift shell with a UIKit widget mapper. Platform surface per the ROADMAP M5 one-liner: APNs, Keychain, universal links, App Store validation.

Context: [docs/planning/MILESTONE.md](docs/planning/MILESTONE.md) (M4 scope boundary) + docs/planning/ROADMAP.md (M5)."

issue "$M5" "phase/p4,type/core" \
"Cross-platform platform APIs" \
"From the ROADMAP M5 one-liner (docs/planning/ROADMAP.md): geolocation, camera, clipboard, share, haptics, biometrics, purchases, background tasks — exposed through the host-registered bridge callback surface established in Phase 3.1."

issue "$M5" "phase/p4,type/navigation" \
"Host-initiated navigation (back button, deep links)" \
"M3 carryover #1: host-initiated navigation over the existing \`Navigate\`/\`CurrentRoute\` plumbing — hardware back button and deep links drive the .NET router, not just the other way around.

Source: [docs/plans/2026-07-10-milestone-3-final-audit.md](docs/plans/2026-07-10-milestone-3-final-audit.md) (carryover table) + [docs/plans/2026-07-10-phase-3.5-conclusion.md](docs/plans/2026-07-10-phase-3.5-conclusion.md)"

# ── M6 — headline items ──────────────────────────────────────────────────────

issue "$M6" "phase/p5,type/components" \
"Component library expansion (BlazorNative.Components)" \
"Grow \`BlazorNative.Components\` beyond the initial \`BnView\`/\`BnText\`/\`BnButton\`/\`BnInput\` quartet (image, scroll, picker, list, …). Includes the M6 packaging-ledger API items deferred from M3: stringly \`FontSize\`/\`Padding\` (source-breaking to change), theme-color fixture single-sourcing, one-type-per-file split.

Source: docs/planning/ROADMAP.md (M6) + [docs/plans/2026-07-10-milestone-3-final-audit.md](docs/plans/2026-07-10-milestone-3-final-audit.md) (carryover table)"

issue "$M6" "phase/p5,type/styling" \
"Styling system (BlazorNative.Styling)" \
"From the ROADMAP M6 one-liner (docs/planning/ROADMAP.md): a real styling system replacing the per-property \`SetStyle\` handling — typed style model, theming beyond the \`CascadingValue<BnTheme>\` demo."

issue "$M6" "phase/p5,type/state" \
"State management (BlazorNative.State)" \
"From the ROADMAP M6 one-liner (docs/planning/ROADMAP.md): a state-management package for BlazorNative apps."

issue "$M6" "phase/p5,type/navigation" \
"Navigation package lift (BlazorNative.Navigation) + real @bind syntax" \
"M6 packaging ledger: lift \`INavigationManager\`/\`NativeNavigationManager\` out of Core/Runtime into a \`BlazorNative.Navigation\` package, and add \`.razor\` compilation so \`@bind-Value\` works as syntax (M3 shipped the \`Value\`/\`ValueChanged\` mechanics only).

Source: docs/planning/ROADMAP.md (M6) + [docs/plans/2026-07-10-milestone-3-final-audit.md](docs/plans/2026-07-10-milestone-3-final-audit.md) (carryover table)"

issue "$M6" "phase/p5,type/tooling" \
"BlazorNative.Cli global tool" \
"From the ROADMAP M6 one-liner (docs/planning/ROADMAP.md): a \`dotnet\` global tool for scaffolding, publishing (per-RID NativeAOT), and deploying BlazorNative apps."

issue "$M6" "phase/p5,type/testing" \
"Full test infrastructure" \
"From the ROADMAP M6 one-liner (docs/planning/ROADMAP.md): test infrastructure for BlazorNative app authors (component test harness, golden-frame helpers, instrumented-test patterns). Includes the M3 test-harness ledger: paired pin-harness extraction, stale-echo sequence-stamping.

Source: docs/planning/ROADMAP.md (M6) + [docs/plans/2026-07-10-milestone-3-final-audit.md](docs/plans/2026-07-10-milestone-3-final-audit.md) (carryover table)"

issue "$M6" "phase/p5,type/ci" \
"CI/CD release pipeline" \
"From the ROADMAP M6 one-liner (docs/planning/ROADMAP.md): tag-triggered release pipeline — build + test, pack NuGet packages, publish (nuget.org decision carried from M4 close), GitHub Release with changelog."

issue "$M6" "phase/p5,type/docs" \
"Documentation site" \
"From the ROADMAP M6 one-liner (docs/planning/ROADMAP.md): a documentation site — getting started, architecture, component reference, bridge/C-ABI reference."

echo ""
echo "  ── Inventory ────────────────────────────────────────────"
echo "  Labels:     $LABEL_COUNT"
echo "  Milestones: $((MILESTONE_CLOSED_COUNT + MILESTONE_OPEN_COUNT)) ($MILESTONE_CLOSED_COUNT closed, $MILESTONE_OPEN_COUNT open)"
echo "  Issues:     $ISSUE_COUNT (open work only — M4 phases + hardening ledger + M5/M6 headliners)"
echo ""
finish_or_fail
ok "All issues created!"
echo ""
echo "  Next steps:"
echo "  1. Open https://github.com/$REPO/issues to see all issues"
echo "  2. Open https://github.com/$REPO/milestones to see the milestones"
echo "  3. Create a GitHub Project board and link the milestones"
echo "  4. Pin the M4 milestone to the repo"
echo ""
