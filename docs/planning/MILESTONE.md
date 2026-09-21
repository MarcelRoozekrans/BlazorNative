# Milestone 14: Twin Divergence, Closed Mechanically

**Status:** active
**Started:** 2026-09-21

**Design:** [`docs/superpowers/specs/2026-09-21-milestone-14-design.md`](../superpowers/specs/2026-09-21-milestone-14-design.md)
**Predecessor:** Milestone 13 — Consumer Ergonomics (complete 2026-08-22, verdict **PASS WITH
FINDINGS**, [audit](../plans/2026-08-22-milestone-13-audit.md)).
**Source:** the **P3 real-device verification run** by @ceesalberts on 2026-09-20 — iPhone 17 Pro
Max, iOS 26, Release `ios-arm64`, signed — reported on [#17][i17] and [#213][i213] and split into
[#338][i338] and [#339][i339]. P3 had been the repo's **single remaining 1.0 blocker** and was
administrative, not technical; it has now reported, and it converted one external blocker into
three concrete engineering ones.

[i17]: https://github.com/MarcelRoozekrans/BlazorNative/issues/17
[i213]: https://github.com/MarcelRoozekrans/BlazorNative/issues/213
[i338]: https://github.com/MarcelRoozekrans/BlazorNative/issues/338
[i339]: https://github.com/MarcelRoozekrans/BlazorNative/issues/339

## Goal

M14 closes the **twin-divergence class** mechanically. The thesis is one sentence: *wherever the
framework holds one truth in two places, either generate the second copy or pin the two against
each other.* M13 named this class, closed four instances by hand, and set a DoD criterion saying a
**new** instance must red — but the mechanism it built covers only half the class, and the P3
device run found the other half by killing a process on real hardware. This milestone builds the
missing half, fixes the three live instances that motivated it, and leaves behind guards that make
the next instance fail in CI rather than on someone's phone.

## The class, stated precisely

| sub-shape | what diverges | mechanism | live instances |
|---|---|---|---|
| **Vocabulary** | the same *names*, hand-copied into three languages | **codegen** from one source — the #262 precedent | **#300** host-event names |
| **Semantics** | the same *name*, different *behaviour* | a **differential pin** comparing the two sides | **#339** `dispatchHostEvent` blocks on iOS, not Android · **#213 item 1** the stored ACL and the read policy disagree |

M13's criterion read *"a generated twin, **or** a pin that compares the two copies"*. **Only the
first clause was ever built.** #339 is the proof the second was load-bearing: no name generator
could have caught it, because the names match perfectly and the behaviour does not.

**#213 is intra-shell** — `BnSecureStorage` disagrees with itself and with `BnBiometrics`, all
inside the Apple shell. The class is therefore not "Android vs iOS"; it is *one truth, two copies,
unpinned*, wherever that occurs.

## Scoping decisions (owner, 2026-09-21)

1. **1.0 is NOT this milestone's DoD.** M14 clears the two P3 blockers and verifies them; whether
   that suffices for 1.0 is a separate owner call afterwards, evidenced by a re-run device
   checklist. Recorded because folding 1.0 in would make the DoD depend on a second device run
   nobody here controls.
2. **#338 surfaces insets to .NET** rather than each shell insetting its own root — so frame parity
   survives as *"same (layout, insets) → same frames"* instead of *"same numbers"*. The only option
   where neither shell has to lie about where `y=0` is.
3. **No ABI change is expected.** Insets are dynamic, so they cannot be an init-time slot; they ride
   the **existing** `blazornative_host_event` export with a generated name — the shape 13.5 already
   found for `themeChanged`. If this proves false it is an explicit scoping decision, not a silent
   one.
4. **#338 is not itself a divergence.** Both shells have *zero* inset handling and are identically
   wrong. It is here as a 1.0 blocker and as the consumer that proves 14.0's generator.
5. **Every phase is independently shippable** and none blocks a 1.0 cut — carried forward from M13,
   because P3's remaining items may clear at any time.

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing — .NET, JVM, **and both device lanes dispatched** (a green *required* set
      does not mean the advisory Android/iOS lanes ran; that is how 11.4's pump bug hid)
- [ ] **The vocabulary sub-shape is closed by generation.** Host-event names emit from
      `src/wire-vocabulary.json` into all three languages; adding a name by hand to one shell reds.
      #300 closed.
- [ ] **The semantic sub-shape is closed by a pin.** A differential guard asserts the two shells'
      dispatch entry points agree on blocking semantics, and **a NEW divergence reds** — not merely
      the two instances already known. This is the milestone's central claim; if no mechanical form
      exists, that is a finding to record explicitly, never a line to quietly drop.
- [ ] **#339 is fixed, and its invisibility is fixed too.** The deadlock is gone *and* at least one
      of the three seams that hid it — `BnAppLifecycle.sinkForTest`'s early return, the camera
      XCTest's `suppressSystemCameraPresentForTest` plus auth overrides, and `BnCamera`'s
      inline-on-main test capture — no longer does.
- [ ] **#338 is fixed on BOTH shells.** Insets reported over `host_event`; Android's identical gap
      closed in the same pass. Frame parity re-expressed as *(layout, insets) → frames* and still
      asserted across shells.
- [ ] **The auth semantics agree and are pinned.** One answer to what `requireAuth` means, the
      stored ACL and the read policy pinned against each other, and the **read-side contract**
      covered — a plain get of an auth-bound item refused with no value leaking. That half was never
      exercised on device, because the demo page exposes no plain-get button.
- [ ] **`Debug` and `Verbose` are observable on a real device**, with the method recorded.
- [ ] **The four documentation landmines are fixed**, `$(AppIdentifierPrefix)` explicitly among them.
- [ ] **No ABI change** — verified by diffing the export surface, not asserted.

> **No "release tagged in git" criterion.** `docs/planning/CONVENTIONS.md` records **`Milestone
> completion tags a release: no`** — release-please owns the `v<semver>` namespace and Phase 8.6
> retired milestone tags. A checkbox nothing will ever tick is a permanent false gap.

## Phases

1. Phase 14.0 — pin the host-event vocabulary [pending]
2. Phase 14.1 — the dispatch twins [pending]
3. Phase 14.2 — safe-area insets to .NET [pending]
4. Phase 14.3 — auth semantics [pending]
5. Phase 14.4 — device observability, docs, and the device lane [pending]
6. Phase 14.5 — audit and close [pending]

**Ordering rationale.** Exactly one hard dependency: **14.0 → 14.2**, because the inset event needs
a generated name and hand-adding a third name to an unpinned vocabulary is the thing this milestone
exists to stop. 14.1, 14.3 and 14.4 are mutually independent. 14.1 is early despite that because it
carries the riskiest claim — the differential pin — and an early failure there is a cheap re-scope,
whereas a late one invalidates the DoD.

## Risk areas

| Risk | Impact | Mitigation |
|---|---|---|
| **The differential pin has no cheap mechanical form** | The DoD's central claim degrades into two hand-patches — precisely M13's failure, repeated | 14.1 attempts it **first** and is early enough to re-scope from. A negative result is recorded as a finding with its evidence, never dropped silently |
| **Insets churn every asserted frame table** | Both shells' parity suites re-baseline; a genuine regression hides in the noise | Land 14.2 **after** 14.1 so the tables move once; read the re-baseline as a reviewed diff, as M13 read its PublicAPI diff as an API review |
| **The auth fix changes behaviour for existing consumers** | An app relying on passcode fallback breaks on upgrade | Pre-1.0 and the surface freezes at 1.0 — the cheap window. Ships with a written migration note whichever way it goes |
| **14.4 stalls on an external contributor** | The device lane slips | The lane is the only externally-dependent item; split the phase rather than block it |
| **#338 needs an ABI change after all** | Scoping decision 3 is wrong and M13's frozen-wire property breaks | The extension policy permits additive growth. Escalate as an explicit decision, and record it |
| **The class is bigger than three instances** | M14 closes what it knows; the fourth ships later | Accepted. The DoD says "a NEW divergence reds", not "these three are fixed" — the mechanism is the deliverable, the instances are its proof |

## Out of scope for this milestone

- **APNs and universal links** — the Apple-account cluster on #17. The device run confirms remote
  push is not testable as shipped and universal links are not implemented.
- **Frame parity for `/layout`, `/scroll`, `/image`** — named in the handover, not compared on
  device for time. Needs hardware.
- **#25 → 1.0 criterion S3** — out of M13 by owner choice; unchanged here.
- **#24 `BlazorNative.Cli`** — the 2026-08-17 audit's verdict stands: it should follow, not lead.
- **A 1.0 cut** — scoping decision 1.

## Open questions

- **14.3's decision is not pre-made.** Align the read to the stored ACL
  (`.deviceOwnerAuthenticationWithBiometrics`, which `BnBiometrics` already uses); relax the ACL to
  `.userPresence`; or keep the behaviour and document that `requireAuth: true` means device-owner
  authentication, passcode included. The hardware result removes the "spurious refusal" argument
  for the first but does not pick between them. **Resolve at the start of 14.3.**
- **What shape should the device CI lane take?** @ceesalberts offered a staging script plus an
  `ios-build`-on-device lane and awaits a preferred shape. **Needs an answer before 14.4 can plan.**

## Audit History

| Date | Verdict | Gaps |
|---|---|---|
| — | *(not yet audited)* | — |
