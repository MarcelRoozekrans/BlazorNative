# Project Conventions

> Written by `init-conventions`. Do not hand-edit — re-run the sub-skill instead; the Commit & Release Protocol reads these fields.

**Established:** 2026-08-19

## Stack

**Language / runtime:** C# / .NET 10 (SDK 10.0.400, pinned in `global.json`) with NativeAOT, Kotlin/JVM (the Android shell), Swift (the Apple shell), Node (the Docusaurus website)
**Package manager:** NuGet, Gradle, npm (`website/`)
**Framework:** Blazor components rendered to native widgets (no WebView)
**Datastore:** n/a

## Commits

**Format:** conventional
**Scopes:** free (defaulted — see the note below)
**Scope source:** n/a
**Fallback when scope not allowed:** omit scope

> **Why `free` and not `enforced`, when a `scope-enum` rule does exist.** `.commitlintrc.yml`
> carries a `scope-enum`, so mechanical detection says `enforced` — but that rule is
> **severity 1, a warning**, and commitlint exits non-zero only on severity 2. The config
> says so itself, in capitals: *"AN UNLISTED SCOPE PASSES … THE SCOPE-ENUM DOES NOT ENFORCE
> ANYTHING — IT DOCUMENTS."* The commit log agrees — `styling`, `tooling`, `testing`,
> `api-stability` and `build` are all merged on `main` and none of them is in the enum.
> Recording `enforced` would make the protocol drop the scope from every orchestration
> commit (`roadmap`, `state`, `milestone`, `sync` are all unlisted), producing bare `chore:`
> subjects for no gate that exists. **The enum still documents the consumer-facing set** —
> it is just not a gate, and this file must not pretend it is. Marked `(defaulted)` because
> it was recorded without an explicit owner confirmation; re-run `init-conventions` to change it.

## Branching

**Model:** feature-branch
**PR required:** yes
**Protected branches:** main

> `main` carries a `required_pull_request_reviews` block (with `required_approving_review_count: 0`
> — a PR is required, approvals are not). The repo squash-merges with
> `squash_merge_commit_title = PR_TITLE`, so **the PR title becomes `main`'s commit subject**
> and is the text release-please parses. That is why the PR title is what `commitlint.yml`
> lints, and why a branch's own commit subjects are not.

## Versioning & Release

**Scheme:** semver
**Released by:** release-please
**Milestone completion tags a release:** no
**Changelog:** auto

> **`Milestone completion tags a release: no` is load-bearing here, not a formality.**
> release-please owns the `v<semver>` namespace and cuts package-release tags; publishing to
> nuget.org happens **inline in `release-please.yml`**, not in a separate step. This repo
> already retired milestone tags once — **Phase 8.6 (2026-07-17) deleted `v1.0`…`v7.0` and
> ruled that no `vN.0` will ever be cut again**; `v8.0` was cancelled, not deferred, and every
> milestone from M9 on has closed on its audit with no tag. `complete-milestone` must therefore
> perform **no git tag action at all** — it announces that the release is handled by
> release-please and stops. See the note at the top of [ROADMAP.md](ROADMAP.md).
>
> Two orphan tags exist (`v0.6.0`, `v0.9.0`) — release-please tagged them, the publish step
> failed the zero-warning pack bar, and the `.nupkg`s 404. Verify a release by HEAD-ing the
> actual `.nupkg`; the version index lags.

## Deployment

**Deploy target:** nuget.org (the eight `BlazorNative.*` packages plus the `dotnet new` template package), GitHub Pages (the Docusaurus site)
**Environments:** github-pages
**Deployed by:** `release-please.yml` (auto-publish inline — `dotnet nuget push --skip-duplicate`, packages then template), `docs.yml` (`actions/deploy-pages@v5`)
