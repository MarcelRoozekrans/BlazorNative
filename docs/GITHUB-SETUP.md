# BlazorNative — GitHub Project Setup Guide

This document explains how to turn the backlog into a fully structured GitHub project with labels, milestones, and issues — ready for community contributions.

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
- **34 labels** (phase, type, contributor difficulty)
- **8 milestones** (one per phase P0–P7)
- **All backlog issues** with correct labels, milestones, and body text

---

## Flags

```bash
# Preview what would be created without actually creating anything
bash scripts/create-github-issues.sh --dry-run

# Only create labels and milestones, skip issues
bash scripts/create-github-issues.sh --labels-only

# Specify repo explicitly (default: auto-detected from git remote)
bash scripts/create-github-issues.sh --repo=ZeroAlloc-Net/BlazorNative
```

---

## Label system

### Phase labels
| Label | Colour | Meaning |
|---|---|---|
| `phase/p0` | 🔴 Red | Blocks everything |
| `phase/p1` | 🟠 Orange | First end-to-end demo |
| `phase/p2` | 🟡 Amber | Real apps possible |
| `phase/p3` | 🟡 Light | Shippable on both platforms |
| `phase/p4` | 🟡 Pale | Full platform coverage |
| `phase/p5` | 🟢 Light green | Developer ecosystem |
| `phase/p6` | 🔵 Light blue | Framework hardening |
| `phase/p7` | 🟣 Lavender | Enterprise readiness |
| `phase/future` | ⚪ Grey | Long-term vision |

### Type labels
`type/core` · `type/android` · `type/ios` · `type/renderer` · `type/components` · `type/styling` · `type/navigation` · `type/state` · `type/http` · `type/analyzer` · `type/tooling` · `type/testing` · `type/ci` · `type/docs` · `type/security` · `type/a11y` · `type/i18n` · `type/perf` · `type/memory` · `type/ota` · `type/compliance` · `type/wit` · `type/nuget`

### Contributor labels
| Label | Meaning |
|---|---|
| `good-first-issue` | Well-scoped, good for newcomers |
| `help-wanted` | Extra attention needed from community |
| `expert-needed` | Requires deep expertise (WASM, Kotlin, Swift) |
| `spike` | Research / investigation required before implementation |
| `blocked` | Blocked on another issue |

---

## GitHub Projects board (manual setup)

After running the script:

1. Go to your GitHub org → **Projects** → **New project**
2. Choose **Board** view
3. Name it `BlazorNative Roadmap`
4. Add columns: `Backlog` · `Up Next` · `In Progress` · `Done`
5. Filter by milestone to see each phase separately
6. Pin the board to the repo

---

## Recommended repo settings

### Branch protection (main)
- Require PR before merging
- Require status checks: `ci / build`, `ci / test`, `ci / wasm`
- Require conversation resolution before merging
- No direct pushes to `main`

### Issue templates
Create `.github/ISSUE_TEMPLATE/` with:
- `bug_report.md` — for bugs
- `feature_request.md` — for new features outside the backlog
- `platform_api.md` — for new platform API proposals (camera, GPS, etc.)

### PR template
Create `.github/pull_request_template.md`:
```markdown
## Related issue
Closes #

## What changed

## How to test

## Platform tested
- [ ] DevHost
- [ ] Android emulator
- [ ] iOS simulator
```

---

## Backlog issues not in the script

The script creates issues for P0–P3 in full detail, and selected P6–P7 items. The remaining P4/P5/P6/P7 items from `BACKLOG.md` are deliberately left out to avoid overwhelming the issue tracker on day one.

**Recommended approach:**
- Start with P0 issues only visible/assigned
- Open P1 issues when P0 milestone closes
- Community members can request issues be opened for specific P4/P5 items they want to work on

This keeps the issue tracker focused rather than showing 170 open issues from day one.

---

## Attracting contributors

Once the repo is live with the issue structure:

1. **Write a good README** — the current `README.md` is a solid start
2. **Add to awesome lists:**
   - [awesome-blazor](https://github.com/AdrienTorris/awesome-blazor)
   - [awesome-dotnet](https://github.com/quozd/awesome-dotnet)
   - [punkpeye/awesome-mcp-servers](https://github.com/punkpeye/awesome-mcp-servers) (for the MCP angle)
3. **Post on:**
   - r/dotnet, r/Blazor, r/wasm
   - Hacker News (Show HN when P1 milestone demo is ready)
   - .NET Foundation Discord
   - Blazor Discord
4. **Tag issues with `good-first-issue`** — GitHub surfaces these to new contributors automatically
5. **Respond fast** — first-time contributors drop off if they don't hear back within 48 hours
