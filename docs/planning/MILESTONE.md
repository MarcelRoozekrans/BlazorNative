# Milestone 17: Close the Dispatch Lane's Debt

**Status:** active
**Started:** 2026-10-14

**Design:** [`docs/superpowers/specs/2026-10-14-milestone-17-design.md`](../superpowers/specs/2026-10-14-milestone-17-design.md)
**Predecessor:** Milestone 16 — Unblock the Dispatch Lane (complete 2026-10-14, verdict **PASS WITH
FINDINGS**, [re-audit](../plans/2026-10-14-milestone-16-reaudit.md)).
**Closes:** #464, #494, #440, #425, #426, #435, #454, #424, #458.

## Goal

M16 freed the shell's dispatch lane and closed with PASS WITH FINDINGS. It left carried defects in
and around that lane:
- a fault from an inline continuation is attributed to the wrong dispatch (#464);
- Android does host-call work on the render thread (#440);
- a late frame can arrive after `retire()` (#425);
- the two shells treat the FaultNotice message differently (#426);
- the slow-handler warning names the wrapper, not the app's handler (#435);
- the local test infrastructure can hang or report a false green (#454, #424, #458).

The 16.8 re-audit also named gaps in 16.7's pins (#494). M17 closes all of it, so the dispatch
lane, its fault path and its Android host calls enter 1.0 with no known defect. **No new
features.** The user-visible outcomes:
- an app author's fault is reported under the handler that raised it;
- an Android biometric or secure-storage call never stalls rendering;
- a recreated Activity never shows a frame from the session it replaced;
- a Release build never prints an exception message to logcat;
- a slow-handler warning names the app's method.

**Chosen 2026-10-14, by owner decision,** over P3 (needs hardware), cross-platform dev parity
(#491, #495) and feature work.

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing — .NET, JVM, **and both device lanes dispatched**, with each lane's
      `headSha` compared against the PR head
- [ ] **#464 is fixed as decided:** a fault from a continuation of dispatch A that runs inline inside
      dispatch B's synchronous part is A's, delivered as A's late FaultNotice under A's handler id,
      and B keeps its own synchronous fault, rc 2. Capture and the begun-call mark both check that
      the flowing scope matches the dispatch's scope. **Both** outcomes are pinned and
      mutation-proven: A's fault reaching A, and B's own fault surviving.
- [ ] **#494 is closed:** every anchor it lists has a recorded red, the unpinned rc claim at
      `FaultNoticeTests.cs:407-410` is pinned or rewritten as resting on reading, and both stale
      headers and the two `host_event` rc tables match the contract.
- [ ] **#440 is fixed:** Android's biometrics `check` and every secure-storage path return from
      `hostCallBegin` at once and complete through `host_call_complete` from a background executor,
      as iOS has since 16.4. A pin shows `hostCallBegin` returns before the work runs.
- [ ] **#425 is fixed: a mount replaces the root.** *Revised 2026-10-15, by owner decision. The
      first plan was a session generation, but there is one process-global session, and the
      stale frames come from the old root that a second mount leaves alive.*
  - A mount disposes any root already mounted, so a late continuation of the old page renders
    nothing.
  - A retired runtime drops any frame it still receives.
  - Recreation is wired: `MainActivity` retires the old runtime, in the reference shell and the
    template, proven on the Android device lane by a rotation test.
  - **A recreated shell stays on its page with its back stack,** owner decision 2026-10-15, which
    answers #425's comment. Navigation from a disposed root's late code is dropped. Both are pinned
    on the same path.
- [ ] **#426 is fixed:** Android redacts the FaultNotice message the way iOS does, and a twin pin
      holds the two shells' redaction together. *Builder's call:* iOS's `privacy: .redacted` is not
      tied to a build configuration, so 17.4's design measures what iOS actually redacts, and when,
      before it settles where Android's redaction lives, the library or the template's `onError`.
- [ ] **#435 is fixed by a forwarding hook:** the slow-handler warning names the app's handler for
      `BnInput`, `BnCheckbox`, `BnPicker`, `BnSlider`, `BnSwitch` and `BnModal`. The KnownLimit test
      `TwoBnInputs_…_KnownLimit` is flipped as its doc says.
- [ ] **#454, #424 and #458 are fixed:**
  - `ReferenceFixtureBase` waits on its child with a bound and fails naming the stalled command;
  - the JVM test task declares the native library as an input;
  - the androidTest `pollUntil` copies are one helper with named failure messages.
- [ ] **No ABI change**, verified by diffing: the 80-byte bridge struct and the 10 exports. Any wire
      addition, such as the frame generation, lives in `src/wire-vocabulary.json` or the existing
      frame format, and is generated like the rest.
- [ ] **Every new pin conforms to [`docs/pin-standard.md`](../../pin-standard.md)** on Rules 2–5
      and 7. Its mutations are recorded, **including its anchors**, and its register row or census
      cell is written **in the phase that adds it**. Both are lessons from the 16.8 re-audit.

> **No "release tagged in git" criterion.** `docs/planning/CONVENTIONS.md` records **`Milestone
> completion tags a release: no`**. release-please owns the `v<semver>` namespace.

## Phases

1. Phase 17.1 — fault attribution (#464, #494) [complete] — a fault is its own dispatch's via the flowing scope; strict-mode loss found by review and fixed; every #494 anchor redded but one, the cancellation fact's rc assertion, `FaultNoticeTests.cs:729`, which no probe-side change can red, as its comment at `:726-728` says; 0.20.0 BREAKING; #500 filed (#503)
2. Phase 17.2 — Android host-call contract (#440) [complete] — one serial executor per handler, after-auth work off main, 5 pins, Android 233 → 238; #506 filed (#507)
3. Phase 17.3 — Activity recreation (#425) [complete] — a mount replaces the root; a recreated shell keeps its page and back stack, a fresh one starts at its own route; a replaced root cannot navigate; a new shell's frames are held until its mount; 15 pins, Android 238 → 242; #511 filed (#510)
4. Phase 17.4 — fault-path parity (#426, #435) [complete] — Android redacts app text in Release logcat like iOS, through one seam and a twin drift pin; the slow-handler warning names the app's handler behind Bn wrappers; Android 242 → 243; #522 filed (#523)
5. Phase 17.5 — test-infrastructure honesty (#454, #424, #458) [complete] — a bounded generator run with a named failure, the native runtime as a Gradle input, one pollUntil; Android 243 → 245; #530 filed (#528)
6. Phase 17.6 — audit and close [complete] — verdict FAIL: item 11 NOT MET, with 18/85 pins unredded, 26 pins with unentered anchors and 3 overstating register cells; #538 and #537 filed (#536)
7. Phase 17.7 — pin-standard gaps (#538, #537) [complete] — every M17 pin has a recorded red under the ratified RS1; #537's hop; retire() proven on device; Android 245 → 247; #543, #544, #551 filed (#545)
8. Phase 17.8 — re-audit and close [complete] — verdict FAIL: item 11 NOT MET on #557, 13 assertion sites without a red across 10 pins plus three overclaiming cells; the population was re-derived from git (#558)
9. Phase 17.9 — pin-standard gaps, second pass (#557) [complete] — 29 sites across 10 pins, all with an RS1 red (17 subject, 12 detector); cells corrected (#560)
10. Phase 17.10 — re-audit and close [active]

> 17.1 goes first because it changes the fault path that 17.4 then redacts and names. 17.2 and
> 17.3 are independent. 17.5 may move earlier if #454 or #424 bites during the others.

## Risk areas

| Risk | Impact | Mitigation |
|---|---|---|
| #425's generation cannot ride the frozen ABI or the existing frame format without a breaking change | 17.3 needs a version bump or a different fix | 17.3's design proves the carrier first, before any code: the frame header, a patch field, or the `register_frame_callback` context. If none works, stop and bring the owner the trade-off rather than breaking the ABI |
| #464's flowing-scope check changes which rc a dispatch returns: B goes from rc 0 to rc 2 in #464's scenario | An app that relied on today's misattribution sees a different rc | Ship as `fix!` with a BREAKING note, as 16.7 did; RcContractCopiesTests keeps the three contract copies in step |
| Moving Android KeyStore work off the render thread (#440) introduces ordering races between `hostCallBegin` and completion | A completion before its begin returns, as 16.4 saw on iOS | Reuse iOS 16.4's design and its pins as the template, and keep `hostCallBegin`'s record-then-enqueue order |
| The forwarding hook (#435) widens the components' internal surface | A new seam that the NOT-API tiering must cover | Keep it `internal`/EditorBrowsable(Never), and check it against `api-tiers` and `PublicAPI` baselines |
| Pins added without register cells, again | The next audit repeats 16.8's finding | DoD item: cells are written in the phase that adds the pin |

## Out of scope

- **P3, iOS on a real device:** the last 1.0 blocker, waiting on hardware and an Apple account.
- **#491 and #495:** cross-platform dev parity.
- **Feature work:** #284, #285, #24.
- **The iOS flakes #444 and #453:** carried. They narrow a test item only if they fire.

## Open questions

None. Every decision was taken on 2026-10-14; see the design.

## Audit History

| Date | Verdict | Gaps |
|---|---|---|
| 2026-10-17 | FAIL | item 11 (#538): 18 of 85 new pins have no recorded red, 9 of them named nowhere; 26 more pins have an anchor no recorded mutation enters; register cells `pin-standard.md:553`, `:554` and `:557` claim more than their record. Items 4, 5, 6, 8 and 9 MET NARROWLY: the #494 cancellation entry, a denied prompt completing on main (#537), #511 and the unproven `retire()` wiring, #522, #530. [Report](../plans/2026-10-17-milestone-17-audit.md) |
| 2026-10-22 | FAIL | item 11 (#557): 13 assertion sites across 10 pins have no red under RS1, counting one assertion at one file:line as a site. Two anchors have no red at all, five anchors rest on fixture or seam reds with no clause-3 argument, and six are main assertions red only on a precondition or another assertion, `SlowHandlerWarningTests.cs:701`–`:702` among them. Register cells `pin-standard.md:501`–`:502` and `:552` claim more than their rows. Items 4, 6, 8 and 9 are MET NARROWLY: the cancellation rc assertion, #511, #522 and #530. Two 17.6 narrowings are lifted: #537, so item 5 is MET, and item 6's unproven `retire()` wiring. The named limits at `GeneratorRunTests :213` and `:83`, both 11 of 15 today, and `RecreationAndroidTest :116`, 6 of 6, are MET NARROWLY. Next: gap phase 17.9. [Report](../plans/2026-10-21-milestone-17-reaudit.md) |
| 2026-10-22 | FAIL | item 11: under the owner's site unit of 2026-10-22, every labelled anchor plus the assertions that carry the test name's claim, 26 sites in 19 pins have no red under RS1 (#561): 22 name-claim sites and the second lines of four two-line anchors, `SlowHandlerWarningTests.cs:623`, `:740`, `:765` and `:785`. Among them, `AndroidHostCallContractTest.kt:147` reds only by a process crash and `PollAndroidTest.kt:40`, `:43` only by an escaping NPE; `FaultNoticeTests.cs:979`, fact 2's Rc2, has none; and `LogPrivacyDriftTests.cs:243`'s red names no line. Register cells `pin-standard.md:558`, `:552` and `:501`–`:502` claim more than their rows: `:558`'s M5 has been an equivalent mutant since `0bc7e6ed`, so `SlowHandlerWarningTests.cs:698` has no live red, though it is not a site under the unit. Items 4, 6, 8 and 9 stay MET NARROWLY: the cancellation rc assertion, #511, #522 and #530; the three named limits are MET NARROWLY; item 2 is MET, provisional on the PR's lanes. The 32 broad-unit sites outside the ruled unit go to M18 (#562). Next: gap phase 17.11. [Report](../plans/2026-10-22-milestone-17-reaudit-2.md) |
