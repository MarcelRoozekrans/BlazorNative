# Session State

**Last session:** 2026-11-16
**Milestone:** 18, Acknowledged Bugs, `[status: active]`, started 2026-10-24.
**Phase:** 18.13, the re-audit, is `[status: active]`. Its PR is #724, from branch `docs/18.13-reaudit`, and it is waiting for the owner.
**Next step:**
1. The owner merges #724.
2. Run `complete-phase` for 18.13.
3. Run `complete-milestone` for M18 as its own PR, with no tag.

## 18.13 outcome

- **Verdict: PASS WITH FINDINGS.** 11 items are MET, 2 are MET NARROWLY, and none is NOT MET. Report: `docs/plans/2026-11-16-milestone-18-reaudit.md`. Record: `docs/plans/2026-11-16-phase-18.13-record.md`.
- **Item 3 is MET NARROWLY** under the owner's amendment of 2026-11-16, which is written into MILESTONE.md item 3. The narrowings:
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

- **#580's GitHub close reason reads `COMPLETED`,** because #721's body said "Closes #580". 18.12 accepted #580; it did not fix it. Proposal: re-close it as "not planned", with a comment pointing to the 18.12 record §7.
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

1. On "Merged ?", check `reviewDecision` and unresolved threads, not only the checks. Then run `complete-phase` for 18.13.
2. Run `complete-milestone` for M18 as its own small PR, with no tag (`CONVENTIONS.md`). The ROADMAP M18 heading becomes complete.
3. Then `new-milestone` for the 1.0 readiness and performance milestone.
4. The standing rules still apply:
   - No Claude signature and no closing keywords unless intended.
   - Ask with AskUserQuestion.
   - Execution is subagent-driven.
   - Never `pkill -f`, and never `&`.
   - JVM pins need a draft PR, because of #491.
