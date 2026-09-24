# Session State

**Last session:** 2026-09-24
**Milestone:** 15 — A Standard for Pins
**Phase:** 15.3 — The first live test: deep-link scheme `[status: active]`
**Branch:** `feat/15.3-deeplink-scheme` → PR **#384** (ready for review, **DO NOT MERGE yet**: head has the red vector but not the fix)
**Plan:** `docs/superpowers/plans/2026-09-24-phase-15.3-deeplink-scheme.md` · **Spec:** `docs/superpowers/specs/2026-09-24-phase-15.3-design.md`

## Current Position

Execution is subagent-driven. The SDD ledger is at `.superpowers/sdd/2026-09-24-phase-15.3-deeplink-scheme/progress.md` (git-ignored, local only). Lane evidence is in `evidence.md` in the same folder.

- **Tasks 1–3 complete and reviewed:** home + `DeepLinkSchemeDriftTests` (.NET 1163 → 1169), the 8th vector `BLAZORNATIVE://settings`, and `BnDeepLinkReachabilityTest` (Android 226 → 228).
- **Red observed** on commit B `24917b4`: [run 36004048485](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36004048485), API 34 AVD. 228 tests, **exactly one** failure: `deep-link vector: BLAZORNATIVE://settings expected:</settings> but was:<null>`. `ios`, `ci` and `commitlint` were green on the same SHA.
- **Reachability measured:** the intent filter resolves `blazornative://` but **not** `BLAZORNATIVE://`, and an explicit intent resolves regardless of scheme. So the fix is **parity hygiene + explicit-intent correctness**, not a browser-visible fix. The PR and CHANGELOG must say so.
- **Task 4 (the fix) is IN PROGRESS and UNCOMMITTED.** The working tree has edits to both `MainActivity.kt` files (shell + template: `data.scheme?.lowercase()` plus a comment) and to `website/docs/migrating/testing-harness.md` (§7 forward pointer). The implementer was stopped by the shutdown before its test run finished and before committing. **These edits are unreviewed and unverified.**
- **Task 5 not started:** ROADMAP outcome block, plan ticks, census aggregate reconciliation, and the final PR body.

## Open Decisions

None blocking. Rulings made during execution are in the SDD ledger (`Ruling:` lines). Carry them into the final message.

## Blockers

None.

## Recommended Next Step

1. `git diff`. Check the three uncommitted files against `.superpowers/sdd/…/task-4-brief.md`, Step 1: the comment block must be verbatim, and shell + template must be byte-identical.
2. Run `dotnet test` (expect 1169) and, from `src/BlazorNative.Jni`, `./gradlew.bat testDebugUnitTest` with **JDK 21** (the default `JAVA_HOME` is a stray JRE 8). Expect 162.
3. Do the mirror proof: revert only the template edit, confirm `TemplateMainActivity_EqualsTheRepos_ModuloTheFallbackAndTheImport` reds, then restore.
4. Commit C (`fix(15.3): compare the deep-link scheme case-insensitively on Android`), task review, push, and confirm `android-instrumented` + `ios` + `ci` are **green on C's exact `headSha`**.
5. Task 5 (record), the final whole-branch review, then `complete-phase` after merge.

Deferred minors for the final review are in the ledger: stripper mutations were full-line only; the census aggregate rows are still at the 15.2 epoch.
