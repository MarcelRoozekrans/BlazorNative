# Session State

**Last session:** 2026-11-17
**Milestone:** 18, Acknowledged Bugs, `[status: complete]` on 2026-11-17, verdict PASS WITH FINDINGS. No tag, per `CONVENTIONS.md`. No milestone is active.
**Phase:** 18.13 is `[status: complete]`. #724 was merged as `0846e515`. The phase and milestone close-out is on branch `chore/complete-m18`.
**Next step:**
1. The owner merges the close-out PR from `chore/complete-m18`.
2. Run `new-milestone` for the 1.0 readiness and performance milestone. Its scope is under "Open for the owner" below.

## 18.13 outcome

- **Verdict: PASS WITH FINDINGS.** 11 items are MET, 2 are MET NARROWLY, and none is NOT MET. Report: `docs/plans/2026-11-16-milestone-18-reaudit.md`. Record: `docs/plans/2026-11-16-phase-18.13-record.md`.
- **Item 3 is MET NARROWLY** under the owner's amendment of 2026-11-16 and the owner's clarification of 2026-11-17, both written into MILESTONE.md item 3. The clarification says the amendment's capture is 18.12's capture as built, which is not required to be shown to catch the unknown cause; CodeRabbit raised this on #724. The narrowings:
  - #407's cause is still unproven after 18.12, and #718 stays open in case it recurs;
  - #453 holds only through #580, which was accepted, not fixed;
  - the evidence is one clean sample per lane;
  - the capture's sampler reads only every 1 to 3 s, so it may miss the cause, and it has no sampler-free control, per the 18.12 record §4.
- **Item 13 is MET NARROWLY,** as in 18.11, on the owner's decision of 2026-11-15.
- **Freshness.** 312 of the 339 M18 sites are fresh and 27 are stale. Every stale site is stale through `Exports.cs`, where the 0.22.1 release changed only `VersionNumber`. All 27 were re-run to their own failing line on both TFMs, and a sample of 4 fresh sites, one per route, reproduced 4 of 4. The register cells now name the stale reds, including the corroborating ones: L1, M5 and R4.
- **Graded lanes:** head `93c7acac`, all four lanes at attempt 1, every failure signature at 0, and the `instrumented-logcat` capture present.
- **Rulings for the owner to check:**
  - Sites are stale through the file a pin is named for, and through the file its mutation edits. This is the conservative reading; under it, rows 14, 15, 19 and 16, 18, 20, 327, 328 are stale.
  - Rows 47 and 48 stay fresh, because `Exports` is only the vehicle there. The record §2 has the reasoning in Disagreements item 6.

## Open for the owner

- **#580 is re-closed as "not planned"** on 2026-11-17, at the owner's choice. A comment on it points to the 18.12 record §7. This settles the report's finding F2.
- The open decisions from M18 carry over to the next milestone:
  - **The 1.0 readiness and performance milestone,** decided 2026-11-04:
    - add a perf baseline: cold boot, dispatch-lane latency and frame-apply time on both shells, compared against an older release;
    - re-check every criterion in `docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md` against current code;
    - then cut 1.0, or list what blocks it.
  - **#621:** a second instance's final destroy denies another instance's orphaned secure-storage prompt.
  - **#600:** biometrics `authenticate` now completes ERROR on a stopped Activity, and that behaviour change is unpinned.
  - **#593:** deliver a faulted batch's remove patches, or document that its diffs are dropped.
  - **#575:** the frame path's two thread-pool hops.
  - **#582:** the allowlist for a second-Activity logcat check.
  - **#711:** about 25 facts with #551's shape.
  - **#716:** the stale `[#444]` trace.
  - **#717:** pre-M18 cells that name stale reds and have no fresh ones.
  - **#718:** #407's trigger, kept open in case it recurs.
  - **#562:** the broader per-statement coverage.

## Blockers

- None.

## Recommended Next Step

1. On "Merged ?" for the close-out PR, check `reviewDecision` and unresolved threads, not only the checks.
2. Then `new-milestone` for the 1.0 readiness and performance milestone, with M18's carried-over issues listed above as candidate scope.
3. The standing rules still apply:
   - No Claude signature and no closing keywords unless intended.
   - Ask with AskUserQuestion.
   - Execution is subagent-driven.
   - Never `pkill -f`, and never `&`.
   - JVM pins need a draft PR, because of #491.
