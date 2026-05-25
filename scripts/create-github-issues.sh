#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# BlazorNative — GitHub Project Setup Script
#
# Creates all labels, milestones, and issues from the backlog on GitHub.
# Run this ONCE after pushing the repo to GitHub.
#
# Prerequisites:
#   • GitHub CLI installed: https://cli.github.com
#   • Authenticated: gh auth login
#   • Run from the repo root: bash scripts/create-github-issues.sh
#
# Flags:
#   --dry-run     Print what would be created without actually creating it
#   --labels-only Only create labels and milestones, skip issues
#   --repo OWNER/REPO  Override repo (default: auto-detected from git remote)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

DRY_RUN=false
LABELS_ONLY=false
REPO=""

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

gh_cmd() {
  if [ "$DRY_RUN" = true ]; then
    echo -e "${GRAY}  [dry-run] gh $*${NC}"
  else
    gh "$@" 2>/dev/null || true
  fi
}

# ── Repo detection ────────────────────────────────────────────────────────────

if [ -z "$REPO" ]; then
  REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || echo "")
  if [ -z "$REPO" ]; then
    echo "Could not detect repo. Pass --repo=OWNER/REPO explicitly."
    exit 1
  fi
fi

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
  gh_cmd label create "$name" \
    --repo "$REPO" \
    --color "$color" \
    --description "$desc" \
    --force   # --force updates if already exists
}

# Phase labels
create_label "phase/p0"      "B60205" "Blocks everything"
create_label "phase/p1"      "D93F0B" "First end-to-end demo"
create_label "phase/p2"      "E4810A" "Real apps possible"
create_label "phase/p3"      "F9D0C4" "Shippable on both platforms"
create_label "phase/p4"      "FEF2C0" "Full platform coverage"
create_label "phase/p5"      "C2E0C6" "Developer ecosystem"
create_label "phase/p6"      "BFD4F2" "Framework hardening"
create_label "phase/p7"      "D4C5F9" "Enterprise readiness"
create_label "phase/future"  "EEEEEE" "Long-term vision"

# Type labels
create_label "type/core"       "0075CA" "Core runtime / bridge"
create_label "type/android"    "3CB371" "Android native shell"
create_label "type/ios"        "8B8B8B" "iOS native shell"
create_label "type/renderer"   "6A4C93" "Headless renderer / patch protocol"
create_label "type/components" "E8A838" "Blazor component library"
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
create_label "type/wit"        "0075CA" "WIT contract"
create_label "type/nuget"      "5319E7" "NuGet packaging"

# Contributor labels
create_label "good-first-issue" "7057FF" "Good for newcomers"
create_label "help-wanted"      "008672" "Extra attention needed"
create_label "expert-needed"    "B60205" "Requires deep expertise"
create_label "spike"            "FBCA04" "Research / investigation required"
create_label "blocked"          "E4E669" "Blocked on another issue"

ok "Labels created"

# ─────────────────────────────────────────────────────────────────────────────
# 2. Milestones
# ─────────────────────────────────────────────────────────────────────────────

echo ""
echo "  ── Milestones ───────────────────────────────────────────"

create_milestone() {
  local title=$1 desc=$2
  log "Milestone: $title"
  gh_cmd api \
    --method POST \
    -H "Accept: application/vnd.github+json" \
    "/repos/$REPO/milestones" \
    -f title="$title" \
    -f description="$desc" \
    -f state="open"
}

create_milestone "P0 — Runtime boots"                  "WASI entry point, async scheduler, renderer internal API strategy"
create_milestone "P1 — First pixel on Android"         "Android Kotlin shell, wasmtime-java, render frame consumer"
create_milestone "P2 — Real apps possible"             "Two-way binding, component library, navigation, full DI wiring"
create_milestone "P3 — Shippable on both platforms"    "iOS shell, CI pipeline, NuGet packages, DevTools inspector"
create_milestone "P4 — Full platform coverage"         "Complete Android/iOS shells, all platform APIs"
create_milestone "P5 — Developer ecosystem"            "Component library, CLI, testing, docs, NuGet"
create_milestone "P6 — Framework hardening"            "Security, crash recovery, accessibility, i18n, performance"
create_milestone "P7 — Enterprise readiness"           "OTA updates, multi-window, compliance, observability"

ok "Milestones created"

if [ "$LABELS_ONLY" = true ]; then
  echo ""
  ok "Done (labels + milestones only)"
  exit 0
fi

# ─────────────────────────────────────────────────────────────────────────────
# 3. Issues
# ─────────────────────────────────────────────────────────────────────────────

echo ""
echo "  ── Issues ───────────────────────────────────────────────"

# Helper: create a single issue
# Usage: issue MILESTONE LABELS TITLE BODY
issue() {
  local milestone=$1 labels=$2 title=$3 body=$4
  log "Issue: $title"
  gh_cmd issue create \
    --repo "$REPO" \
    --title "$title" \
    --body "$body" \
    --label "$labels" \
    --milestone "$milestone"
  sleep 0.3  # avoid secondary rate limit
}

# ── P0 ────────────────────────────────────────────────────────────────────────

M="P0 — Runtime boots"
L="phase/p0"

issue "$M" "$L,type/core,spike" \
"Renderer internal API access strategy" \
"## Problem
\`RenderTreeDiff\`, \`RenderTreeFrame\`, \`RenderBatch\` are \`internal\` in \`Microsoft.AspNetCore.Components\`. \`NativeRenderer\` cannot access them without a strategy.

## Options to evaluate
- **Option A:** \`InternalsVisibleTo\` — requires a Blazor fork. Not ideal.
- **Option B:** \`BindingFlags.NonPublic\` reflection — violates AOT.
- **Option C:** Public \`Renderer\` API only — may be too limited.
- **Option D:** \`UnsafeAccessor\` (new in .NET 8) — AOT-safe, no fork. ✅ preferred

## Acceptance criteria
- [ ] Spike completed, option chosen and documented
- [ ] \`NativeRenderer.cs\` updated to use chosen approach
- [ ] Compiles cleanly under \`wasi-wasm\` AOT target

## References
- [\`UnsafeAccessor\` docs](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.unsafeaccessorattribute)
- See \`docs/SESSION-HISTORY.md\` for full context"

issue "$M" "$L,type/core" \
"WASI Program.cs entry point and async bootstrap" \
"## Problem
There is no \`Program.cs\` for the \`wasi-wasm\` target. The WASM module has nothing to execute when wasmtime loads it.

## Requirements
- Bootstrap .NET cooperative async scheduler via \`WasiEventLoop.Run()\`
- Initialise DI container
- Register \`WasiBridge\`, \`AddBlazorNativeRenderer()\`, \`AddBlazorNativeHttp()\`
- Mount root Blazor component
- Correct call order is critical — one wrong ordering and \`await\` never resumes

## Acceptance criteria
- [ ] \`src/BlazorNative.Core/WasiEntryPoint.cs\` created
- [ ] \`await Task.Delay(1)\` round-trip works via wasmtime
- [ ] \`dotnet build -r wasi-wasm\` succeeds

## References
- [dotnet-wasi-sdk samples](https://github.com/dotnet/dotnet-wasi-sdk)
- Depends on: #renderer-internal-api-strategy"

issue "$M" "$L,type/core" \
"[UnmanagedCallersOnly] export wiring" \
"## Problem
\`WasiBridge.DispatchEventNative\` is declared with \`[UnmanagedCallersOnly(EntryPoint = \"blazornative_dispatch_event\")]\` but the export may not appear in the compiled \`.wasm\` module's export table.

## Acceptance criteria
- [ ] \`wasm-tools dump BlazorNative.Core.wasm\` shows \`blazornative_dispatch_event\` in exports
- [ ] Native shell can call the export and the event is received by \`WasiBridge\`

## Verification
\`\`\`bash
make wasi
wasm-tools dump artifacts/wasi/BlazorNative.Core.wasm | grep export
\`\`\`"

issue "$M" "$L,type/core" \
"DispatchEventAsync signature fix" \
"## Problem
\`WebEventData\` wrapper in \`RendererServices.cs\` doesn't match Blazor's internal \`DispatchEventAsync(ulong handlerId, EventFieldInfo? fieldInfo, EventArgs eventArgs)\` signature.

## Acceptance criteria
- [ ] \`DispatchEventAsync\` call compiles and routes events correctly
- [ ] \`@onclick\` on a \`BnButton\` fires the correct Blazor handler
- [ ] Depends on renderer internal API strategy being resolved first"

issue "$M" "$L,type/core,good-first-issue" \
"Cooperative async scheduler validation" \
"## Task
Validate that the .NET cooperative async scheduler works correctly on the \`wasi-wasm\` target.

## Acceptance criteria
- [ ] \`await Task.Delay(100)\` completes correctly via wasmtime
- [ ] \`await Task.Yield()\` yields and resumes correctly
- [ ] Multiple concurrent async operations interleave correctly
- [ ] Document any gotchas in \`docs/SESSION-HISTORY.md\`"

# ── P1 ────────────────────────────────────────────────────────────────────────

M="P1 — First pixel on Android"
L="phase/p1"

issue "$M" "$L,type/android" \
"Android Kotlin shell — project scaffold" \
"## Task
Create \`src/BlazorNative.Shell.Android/\` as an Android project.

## Requirements
- Minimal \`MainActivity.kt\` that loads \`.wasm\` binary from app assets
- Gradle build file with wasmtime-java dependency
- Basic Android manifest
- \`.wasm\` loaded from \`assets/blazornative.wasm\`

## Acceptance criteria
- [ ] Project builds via \`./gradlew assembleDebug\`
- [ ] App launches on Android emulator (API 26+)
- [ ] \`.wasm\` binary loads without crash"

issue "$M" "$L,type/android,expert-needed" \
"wasmtime-java JNI integration" \
"## Task
Embed wasmtime-java and load the BlazorNative WASM module.

## Requirements
- Add \`dev.wasmtime:wasmtime-java\` via Gradle
- Load compiled \`BlazorNative.Core.wasm\` from assets
- Wire up \`mobile_bridge\` WASM import symbols to Kotlin implementations
- Handle module load errors gracefully

## References
- [wasmtime-java](https://github.com/bytecodealliance/wasmtime-java)

## Acceptance criteria
- [ ] WASM module loads successfully
- [ ] A simple string round-trip call works (WASM calls \`shell_platform_info\`, receives response)"

issue "$M" "$L,type/android" \
"mobile_bridge symbol exports (Android)" \
"## Task
Implement all 7 symbols the WASM module imports in Kotlin.

## Symbols required
- \`shell_navigate(routePtr, routeLen)\`
- \`shell_current_route(buf, bufLen) → int\`
- \`shell_storage_read(keyPtr, keyLen, valBuf, valBufLen) → int\`
- \`shell_storage_write(keyPtr, keyLen, valPtr, valLen)\`
- \`shell_storage_delete(keyPtr, keyLen)\`
- \`shell_fetch(reqPtr, reqLen, resBuf, resBufLen) → int\`
- \`shell_platform_info(buf, bufLen) → int\`

## Acceptance criteria
- [ ] All 7 symbols implemented and callable from WASM
- [ ] String encoding/decoding (UTF-8) correct across the boundary
- [ ] \`shell_fetch\` makes a real HTTP request and returns the response"

issue "$M" "$L,type/android,type/renderer" \
"Render frame consumer (Android)" \
"## Task
Android shell receives \`RenderFrame\` JSON and applies patches to the native widget tree.

## Requirements
- Receive \`RenderFrame\` from WASM (via storage write hook or dedicated export)
- Parse \`RenderPatch[]\` list
- Apply patches atomically on the UI thread after \`CommitFramePatch\`
- Handle unknown \`op\` values gracefully (log + skip)

## Acceptance criteria
- [ ] A \`BnText\` component renders as a \`TextView\` on screen
- [ ] Text content updates when Blazor re-renders"

issue "$M" "$L,type/android,good-first-issue" \
"Native widget mapper (Android)" \
"## Task
Map BlazorNative \`NodeType\` strings to Android widget classes.

## Mapping required
| NodeType | Android widget |
|---|---|
| \`view\` | \`FrameLayout\` |
| \`text\` | \`TextView\` |
| \`button\` | \`Button\` |
| \`input\` | \`EditText\` |
| \`image\` | \`ImageView\` |
| \`scroll\` | \`ScrollView\` |
| \`picker\` | \`Spinner\` |

## Acceptance criteria
- [ ] All 7 node types create correct Android widgets
- [ ] Widgets are added/removed from the view hierarchy correctly"

issue "$M" "$L,type/core,type/renderer" \
"BlazorNativeHostElement stub" \
"## Problem
\`Renderer.AddRootComponent\` needs a host element descriptor. Without it, the root component cannot be mounted.

## Acceptance criteria
- [ ] \`BlazorNativeHostElement\` stub created
- [ ] Root component mounts successfully in DevHost
- [ ] Root component mounts successfully in WASI target"

# ── P2 ────────────────────────────────────────────────────────────────────────

M="P2 — Real apps possible"
L="phase/p2"

issue "$M" "$L,type/renderer" \
"@bind two-way binding" \
"## Problem
Input value changes from native (\`EditText.afterTextChanged\`) need to flow back into Blazor component state.

## Requirements
- Native shell dispatches \`NativeUiEvent(nodeId, handlerId, \"change\", value)\` on input change
- \`NativeRenderer.DispatchUiEventAsync\` routes it to the correct Blazor \`@bind\` handler
- \`ChangeEventArgs.Value\` contains the new input value

## Acceptance criteria
- [ ] \`<BnInput @bind-Value=\"Name\" />\` updates component state on Android
- [ ] Re-render triggered correctly after state change"

issue "$M" "$L,type/components" \
"BnView / BnText / BnButton / BnInput component library (initial set)" \
"## Task
Create \`src/BlazorNative.Components/\` with the core set of native components.

## Components required (v1)
- \`BnView\` — layout container
- \`BnText\` — text display
- \`BnButton\` — tappable button with \`OnClick\`
- \`BnInput\` — text input with \`@bind-Value\`
- \`BnImage\` — image display with \`Src\`
- \`BnScroll\` — scrollable container

## Acceptance criteria
- [ ] All components emit correct \`NodeType\` in patch protocol
- [ ] No HTML element leakage into native render path
- [ ] Components work identically in DevHost and WASI target"

issue "$M" "$L,type/core,good-first-issue" \
"DI fully wired end-to-end" \
"## Task
Wire up all DI registrations in both DevHost and WASI entry point.

## Requirements
- DevHost \`Program.cs\` calls \`AddBlazorNativeHttp()\` and \`AddBlazorNativeRenderer()\`
- \`NativeRenderer\` is mounted and rendering in DevHost
- Render frames appear at \`GET /dev/renderframe\` endpoint
- WASI entry point has equivalent setup

## Acceptance criteria
- [ ] \`make dev\` starts and renders the Home component
- [ ] Render frames visible in DevTools"

issue "$M" "$L,type/navigation" \
"Navigation service (INativeNavigator)" \
"## Task
Higher-level navigation service on top of \`IMobileBridge.NavigateAsync\`.

## Requirements
- \`INativeNavigator\` with \`GoToAsync<TPage>()\`, \`GoBackAsync()\`, \`CanGoBack\`
- Back stack management
- Route parameter passing
- Transition hints (\`Slide\`, \`Fade\`, \`Modal\`, \`None\`)
- \`[Route(\"/path/{id}\")]\` attribute + source-generated route table

## Acceptance criteria
- [ ] Navigation between two pages works on Android
- [ ] Back navigation works (hardware + gesture)
- [ ] Route parameters received by target page"

issue "$M" "$L,type/components,good-first-issue" \
"BlazorNativeComponentBase" \
"## Task
Base class that pre-injects the bridge and provides common helpers.

\`\`\`csharp
public class BlazorNativeComponentBase : ComponentBase
{
    [Inject] protected IMobileBridge Bridge { get; set; } = null!;
    [Inject] protected INativeNavigator Nav { get; set; } = null!;

    protected Task NavigateAsync(string route) => Bridge.NavigateAsync(route);
    protected Task<string?> ReadStorageAsync(string key) => Bridge.ReadStorageAsync(key).AsTask();
    protected Task<T?> FetchJsonAsync<T>(string url) { ... }
}
\`\`\`

## Acceptance criteria
- [ ] All sample components inherit from \`BlazorNativeComponentBase\`
- [ ] No direct \`@inject IMobileBridge\` needed in components"

issue "$M" "$L,type/renderer" \
"Cascading values support" \
"## Task
Wire up \`CascadingValue<T>\` in the headless renderer.

## Acceptance criteria
- [ ] Theme context cascades correctly through component tree
- [ ] Auth context available via \`[CascadingParameter]\` in nested components
- [ ] Works identically in DevHost and WASI target"

# ── P3 ────────────────────────────────────────────────────────────────────────

M="P3 — Shippable on both platforms"
L="phase/p3"

issue "$M" "$L,type/analyzer,type/testing,good-first-issue" \
"Analyzer unit tests (BN0001-BN0013)" \
"## Task
Add \`Microsoft.CodeAnalysis.Testing\` based tests for all diagnostics.

## Tests required (one per diagnostic)
- BN0001 — \`new Thread()\` → error
- BN0002 — \`Task.Run()\` → error
- BN0003 — \`Parallel.For\` → error
- BN0004 — \`Thread.Sleep\` → error
- BN0005 — \`Mutex\` → warning
- BN0006 — \`[ThreadStatic]\` → warning
- BN0010 — Socket APIs → error
- BN0011 — \`new HttpClient()\` → warning
- BN0012 — \`File.*\` → warning
- BN0013 — \`Process.*\` → error

Each test: fires on bad code ✓, silent on correct code ✓"

issue "$M" "$L,type/ci" \
"GitHub Actions CI pipeline" \
"## Task
Create \`.github/workflows/ci.yml\`.

## Steps required
- Build all projects (\`dotnet build\`)
- Run analyzers
- Run tests (\`dotnet test\`)
- Build \`wasi-wasm\` target
- Validate \`.wasm\` with \`wasm-tools validate\`
- Upload \`.wasm\` as build artifact

## Acceptance criteria
- [ ] CI passes on every PR
- [ ] WASM binary artifact available for download after each run"

issue "$M" "$L,type/ios,expert-needed" \
"iOS Swift shell" \
"## Task
Create \`src/BlazorNative.Shell.iOS/\` — same WIT contract as Android, implemented in Swift.

## Requirements
- Xcode project with Swift Package Manager
- WasmKit or wasmtime-swift embed
- All 7 \`mobile_bridge\` symbols implemented in Swift
- UIKit widget mapper
- Render frame consumer

## References
- [WasmKit](https://github.com/swiftwasm/WasmKit)

## Acceptance criteria
- [ ] App runs on iOS simulator
- [ ] Same Blazor components render on iOS as Android"

issue "$M" "$L,type/tooling,good-first-issue" \
"DevTools render tree inspector" \
"## Task
Add render tree inspection to the DevTools API and browser UI.

## Requirements
- \`GET /dev/rendertree\` — current widget tree as JSON
- \`GET /dev/frames\` — SSE stream of live render frames
- Browser UI at \`/dev\` showing:
  - Live patch stream
  - Widget tree (collapsible)
  - Event log
  - Storage state

## Acceptance criteria
- [ ] Open \`https://localhost:5273/dev\` in browser
- [ ] Widget tree updates in real time as components re-render"

issue "$M" "$L,type/wit,good-first-issue" \
"wit-bindgen C# output generation" \
"## Task
Run \`wit-bindgen\` and commit generated C# bindings.

## Steps
\`\`\`bash
make wit-gen
\`\`\`

## Acceptance criteria
- [ ] Generated files committed to \`src/BlazorNative.Bridge/Generated/\`
- [ ] Generated bindings match \`WasiBridge.cs\` manual implementation
- [ ] \`make wit-gen\` added to CI pipeline"

issue "$M" "$L,type/nuget" \
"NuGet packaging (all packages)" \
"## Task
Package all BlazorNative libraries for NuGet distribution.

## Packages
- \`BlazorNative.Core\`
- \`BlazorNative.Renderer\`
- \`BlazorNative.Http\`
- \`BlazorNative.Analyzers\` (special \`.props\`/\`.targets\` packaging required)
- \`BlazorNative.Components\`

## Acceptance criteria
- [ ] All packages publish to NuGet.org
- [ ] Analyzer package auto-registers in consuming projects
- [ ] \`dotnet add package BlazorNative.Core\` works end-to-end"

issue "$M" "$L,type/analyzer,good-first-issue" \
".editorconfig analyzer scoping" \
"## Problem
WASI analyzers (BN0001-BN0013) fire false positives in DevHost and test projects.

## Fix
Scope analyzers to only activate when \`RuntimeIdentifier\` is \`wasi-wasm\`.
Add \`.editorconfig\` suppressions for non-WASI projects.

## Acceptance criteria
- [ ] No BN diagnostics in DevHost project
- [ ] No BN diagnostics in test projects
- [ ] BN diagnostics still fire in \`BlazorNative.Core\` when targeting \`wasi-wasm\`"

issue "$M" "$L,type/ci" \
"GitHub Actions release pipeline" \
"## Task
Create \`.github/workflows/release.yml\`.

## Steps
- Triggered on version tag (\`v*\`)
- Build + test
- Pack NuGet packages
- Publish to NuGet.org
- Create GitHub Release with CHANGELOG extract
- Attach \`.wasm\` binary to release

## Acceptance criteria
- [ ] Push \`v0.1.0\` tag → packages appear on NuGet.org within 5 minutes"

# ── P6 ────────────────────────────────────────────────────────────────────────

M="P6 — Framework hardening"
L="phase/p6"

issue "$M" "$L,type/security,expert-needed" \
"WASM binary signature verification" \
"## Task
Native shell validates SHA-256 + Ed25519 signature on the \`.wasm\` binary before loading.

## Requirements
- Signing step in release CI pipeline
- Android shell verifies signature before \`wasmtime\` loads module
- iOS shell same
- Invalid signature → refuse to load, show error

## Acceptance criteria
- [ ] Tampered \`.wasm\` rejected by native shell
- [ ] Signing documented in release pipeline"

issue "$M" "$L,type/security" \
"Bridge URL allowlist" \
"## Task
\`shell_fetch\` enforces a configurable allowlist of permitted hosts.

## Requirements
- Allowlist defined in \`blazornative.json\` at build time
- Native shell rejects calls to non-allowlisted hosts before network
- DevHostBridge logs rejected calls with clear message
- Wildcard subdomain support (\`*.myapi.com\`)

## Acceptance criteria
- [ ] Call to non-allowlisted URL returns \`BridgeError.PermissionDenied\`
- [ ] Allowlist configurable without recompiling native shell"

issue "$M" "$L,type/security,good-first-issue" \
"DevTools endpoint security (localhost-only + debug-only)" \
"## Task
Secure the DevTools REST API.

## Requirements
- \`/dev/*\` routes only active in \`Debug\` builds
- Bind to \`localhost\` only (not \`0.0.0.0\`) in all cases
- \`Release\` builds: entire \`/dev\` route group removed at compile time

## Acceptance criteria
- [ ] \`Release\` build returns 404 for all \`/dev\` routes
- [ ] \`Debug\` build \`/dev\` not accessible from another machine on LAN"

issue "$M" "$L,type/core,expert-needed" \
"WASM crash isolation and module restart" \
"## Task
If WASM module traps or throws unhandled exception, native shell catches it and restarts without killing the app.

## Requirements
- Native shell wraps all WASM calls in trap handler
- On trap: log crash, serialise stack trace, attempt restart
- Max restart attempts configurable (default: 3)
- After max restarts: show fallback error UI
- Crash report dispatched to \`ICrashReporter\`

## Acceptance criteria
- [ ] Deliberate \`throw new Exception()\` in WASM → app shows retry UI, not crash
- [ ] Crash report received with stack trace"

issue "$M" "$L,type/a11y" \
"Screen reader bridge interface" \
"## Task
Extend \`mobile-bridge.wit\` and patch protocol with accessibility annotations.

## Requirements
- \`set_accessibility_label(nodeId, label)\`
- \`set_accessibility_hint(nodeId, hint)\`
- \`set_accessibility_role(nodeId, role)\`
- Android: \`contentDescription\` + \`ViewCompat.setAccessibilityDelegate\`
- iOS: \`accessibilityLabel\` + \`accessibilityTraits\`
- \`BnAccessibility\` component attributes: \`Label\`, \`Hint\`, \`Role\`, \`IsHidden\`

## Acceptance criteria
- [ ] TalkBack (Android) reads \`BnButton\` label correctly
- [ ] VoiceOver (iOS) reads \`BnButton\` label correctly"

issue "$M" "$L,type/a11y,good-first-issue" \
"Dynamic text size support" \
"## Task
Respond to system font scale changes.

## Requirements
- Native shell detects font scale change → \`NativeEvent(\"fontScaleChanged\", scale)\`
- \`BnText\` components scale \`FontSize\` proportionally
- Layout system reflows after scale change

## Acceptance criteria
- [ ] Increase system font size → \`BnText\` content scales correctly"

issue "$M" "$L,type/i18n" \
"Locale detection and BridgeGlobalization" \
"## Task
Fix locale-sensitive formatting broken by \`InvariantGlobalization=true\`.

## Requirements
- \`PlatformInfo.Locale\` populated from device locale by native shell
- \`BridgeGlobalization\` service delegates formatting to native shell
- \`FormatCurrency(amount, locale)\`, \`FormatDate(date, locale)\`, \`FormatNumber(n, locale)\`

## Acceptance criteria
- [ ] \`€1.234,56\` formatted correctly for Dutch locale
- [ ] \`01/05/2026\` vs \`05/01/2026\` correct per locale"

issue "$M" "$L,type/i18n" \
"RTL layout support" \
"## Task
Support right-to-left layout for Arabic, Hebrew, etc.

## Requirements
- \`PlatformInfo.IsRtl\` flag from native shell
- \`BnView\` flips layout direction automatically
- \`layoutDirection: ltr|rtl\` added to \`CreateNodePatch\`
- Android: \`ViewCompat.setLayoutDirection\`
- iOS: \`semanticContentAttribute\`

## Acceptance criteria
- [ ] Arabic locale → layout mirrors correctly on both platforms"

issue "$M" "$L,type/perf" \
"Frame timing instrumentation (BlazorNative.Diagnostics)" \
"## Task
Measure render frame latency end-to-end.

## Metrics to capture
- \`UpdateDisplayAsync\` called → \`RenderFrame\` serialised
- \`RenderFrame\` dispatched → \`CommitFramePatch\` acknowledged
- Total frame time

## Requirements
- \`IFrameMetrics\` service available in components
- \`GET /dev/metrics\` in DevHost
- Slow frame detection (>16ms flagged)
- Zero overhead when \`BlazorNative.Diagnostics\` not referenced

## Acceptance criteria
- [ ] Frame timings visible in DevTools
- [ ] Slow frames highlighted in amber/red"

issue "$M" "$L,type/memory" \
"Bridge buffer pooling" \
"## Task
Replace per-call \`byte[]\` allocations in \`WasiBridge\` with \`ArrayPool<byte>\`.

## Impact
Every bridge call currently allocates. On the render hot path this is significant GC pressure.

## Acceptance criteria
- [ ] \`WasiBridge\` uses \`ArrayPool<byte>.Shared\` for all bridge buffers
- [ ] No allocation on steady-state render path (verify with dotMemory / BenchmarkDotNet)"

issue "$M" "$L,type/wit" \
"WIT versioning strategy and typed error returns" \
"## Task
Harden the WIT contract for long-term compatibility.

## Requirements
- \`@since(version = \"0.1.0\")\` annotations on all interfaces
- Compatibility policy documented: minor = additive only, major = migration required
- Replace negative int return codes with \`result<T, error-kind>\`
- \`error-kind\` enum: \`timeout\`, \`not-found\`, \`permission-denied\`, \`network-error\`, \`serialization-error\`
- Version negotiation handshake on module load

## Acceptance criteria
- [ ] WIT file updated with version annotations and typed errors
- [ ] C# and Kotlin bindings regenerated
- [ ] Version mismatch detected and reported on load"

# ── P7 ────────────────────────────────────────────────────────────────────────

M="P7 — Enterprise readiness"
L="phase/p7"

issue "$M" "$L,type/ota,expert-needed" \
"OTA update protocol" \
"## Task
Allow \`.wasm\` binary updates without a full App Store release.

## Requirements
- App checks configurable endpoint for new version on launch
- Downloads + verifies Ed25519 signature
- Stores new version alongside current
- Swaps on next cold start
- Max 2 versions stored
- Rollback if new version crashes within 30s of launch
- \`NativeEvent(\"updateAvailable\")\` and \`NativeEvent(\"updateDownloaded\")\` for in-app UI

## Acceptance criteria
- [ ] Push new \`.wasm\` to update endpoint → app uses new version on next launch
- [ ] Corrupt \`.wasm\` → rollback to previous version automatically"

issue "$M" "$L,type/ota" \
"Delta WASM updates" \
"## Task
Ship only changed WASM sections rather than full binary on updates.

## Requirements
- Release pipeline generates \`bsdiff\`/\`zstd\` delta between versions
- Native shell applies delta to stored \`.wasm\` binary
- Falls back to full download if delta fails

## Acceptance criteria
- [ ] Minor release update payload <20% of full binary size"

issue "$M" "$L,type/compliance,good-first-issue" \
"SBOM generation" \
"## Task
Generate CycloneDX SBOM in release pipeline.

## Requirements
- CycloneDX SBOM listing all dependencies
- Published as \`sbom.json\` alongside each GitHub Release
- Automated via GitHub Actions \`release.yml\`

## Acceptance criteria
- [ ] \`sbom.json\` attached to every release
- [ ] All direct and transitive dependencies listed"

issue "$M" "$L,type/compliance,good-first-issue" \
"Open source license audit" \
"## Task
Audit all dependencies for license compatibility.

## Dependencies to audit
- wasmtime (Apache 2.0)
- .NET runtime (MIT)
- Blazor (MIT)
- wasmtime-java (Apache 2.0)
- WasmKit (Apache 2.0)
- All NuGet transitive dependencies

## Acceptance criteria
- [ ] \`LICENSES.md\` created listing all dependencies and their licenses
- [ ] No copyleft (GPL) dependencies
- [ ] Any non-MIT/Apache dependencies flagged and reviewed"

issue "$M" "$L,type/perf" \
"Structured logging pipeline (IBlazorNativeLogger)" \
"## Task
In-WASM logging that routes to platform logging via bridge.

## Requirements
- \`IBlazorNativeLogger\` service available in WASM components
- \`shell_log(level, category, message, structuredJson)\` bridge call
- Android: routes to Logcat
- iOS: routes to OSLog
- DevHost: routes to \`ILogger<T>\` (console)
- Optional remote sink (configurable endpoint)

## Acceptance criteria
- [ ] \`logger.LogInformation(\"User navigated to {Route}\", route)\` appears in Logcat"

issue "$M" "$L,type/perf,good-first-issue" \
"Performance budget enforcement in CI" \
"## Task
Fail CI if performance budgets are exceeded.

## Budgets
- WASM binary size: <10MB compressed (configurable)
- P95 frame time: <16ms in integration tests

## Acceptance criteria
- [ ] CI fails with clear message if \`.wasm\` exceeds size budget
- [ ] CI fails if frame timing regression detected"

echo ""
ok "All issues created!"
echo ""
echo "  Next steps:"
echo "  1. Open https://github.com/$REPO/issues to see all issues"
echo "  2. Open https://github.com/$REPO/milestones to see phase milestones"
echo "  3. Create a GitHub Project board and link the milestones"
echo "  4. Pin the P0 milestone to the repo"
echo ""
