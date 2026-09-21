# Milestone 14 Design — Twin Divergence, Closed Mechanically

**Date:** 2026-09-21
**Milestone:** 14
**Stage:** milestone (one milestone, multiple phases)

**Predecessor:** Milestone 13 — Consumer Ergonomics (complete 2026-08-22, verdict PASS WITH
FINDINGS; audit at [`docs/plans/2026-08-22-milestone-13-audit.md`](../../plans/2026-08-22-milestone-13-audit.md)).
**Source:** the **P3 real-device verification run** by @ceesalberts on 2026-09-20 — iPhone 17 Pro
Max (`iPhone18,2`), iOS 26, Release `ios-arm64`, signed — reported on
[#17](https://github.com/MarcelRoozekrans/BlazorNative/issues/17) and
[#213](https://github.com/MarcelRoozekrans/BlazorNative/issues/213), split into
[#338](https://github.com/MarcelRoozekrans/BlazorNative/issues/338) and
[#339](https://github.com/MarcelRoozekrans/BlazorNative/issues/339).

## Goal

M14 closes the **twin-divergence class** mechanically. The thesis is one sentence: *wherever the
framework holds one truth in two places, either generate the second copy or pin the two against
each other.* M13 named this class, closed four instances by hand, and set a DoD criterion saying a
**new** instance must red — but the mechanism it built covers only half the class, and the P3
device run found the other half by killing a process on real hardware. This milestone builds the
missing half, fixes the three live instances that motivated it, and leaves behind guards that make
the next instance fail in CI rather than on someone's phone.

## The class, stated precisely

The class has **two sub-shapes**. Conflating them is why M13's mechanism did not reach.

| sub-shape | what diverges | mechanism that closes it | live instances |
|---|---|---|---|
| **Vocabulary** | the same *names*, hand-copied into three languages | **codegen** from one source — the [#262](https://github.com/MarcelRoozekrans/BlazorNative/issues/262) precedent, which retired four of five vocabularies | **#300** — host-event names |
| **Semantics** | the same *name*, different *behaviour* | a **differential pin** comparing the two sides | **#339** — `dispatchHostEvent` blocks on iOS, not on Android · **#213 item 1** — the stored ACL and the read policy disagree |

M13's criterion read *"a generated twin, **or** a pin that compares the two copies"*. Only the
first clause was ever built. #339 is the proof that the second clause was load-bearing: no name
generator could have caught it, because the names match perfectly and the **behaviour** does not.

Note that #213 is **intra-shell** — `BnSecureStorage` disagrees with itself and with
`BnBiometrics`, all inside the Apple shell. The class is therefore not "Android vs iOS". It is
"one truth, two copies, unpinned", wherever that occurs.

### The three instances, with evidence

**#339 — `dispatchHostEvent` (semantic, cross-shell).** Verified in the tree:

| | Android — `BlazorNativeRuntime.kt:230` | iOS — `BnRuntime.swift:320` |
|---|---|---|
| `dispatchHostEvent` | `dispatchLane.execute { }` — fire-and-forget | `dispatchLane.sync { }` — blocks the caller |
| blocking variant | `dispatchHostEventAndWait` — a **separate** method | none; there is only the blocking one |

Android's lifecycle path (`MainActivity.kt:358-375`) calls the non-blocking method. iOS's lifecycle
path calls the only method it has, which blocks the main thread on the lane. The Swift doc comment
justifies the `sync` with *"so the re-route swap's frames are applied before it returns"* — correct
for the **deep-link/navigate** caller it was written for, wrong for the **lifecycle** caller later
pointed at it. Kotlin's KDoc for its blocking variant states the hazard outright: *"Safe from any
thread EXCEPT the dispatch lane itself (a call from the lane would self-deadlock)."* The Swift twin
carries no such warning.

The observed failure: an async host call holds the serial lane, a permission alert fires
`willResignActive`, the lifecycle event blocks main on the held lane, and the capability's
completion needs main — so the lane is never freed and main never wakes. The device log shows the
TCC grant arriving 2.1s after the main thread's last line, and being acted on never.

**#300 — host-event names (vocabulary).** Already identified in phase 13.5 as a prerequisite for
theming. It is now **also** the prerequisite for #338's inset event. Two features blocked by the
same unpinned vocabulary is the argument for generating it rather than adding a third name by hand.

**#213 item 1 — auth semantics (semantic, intra-shell).** `BnSecureStorage.swift:219` stores under
`.biometryCurrentSet`; `:301` reads with `.deviceOwnerAuthentication`; `BnBiometrics.swift:167`
uses `.deviceOwnerAuthenticationWithBiometrics`. The device run showed iOS 26 resolves the
mismatch **permissively**: `KofN(k:1)` over `{Passcode, Pearl}`, so a secret declared
enclave-and-biometry-bound is readable with the device passcode, and nothing in the API surface,
the returned status, or the demo echo says so. The predicted symptom (a spurious `AuthFailed`
after a successful passcode fallback) did **not** reproduce; the mismatch is real and its
consequence is inverted — weaker than declared rather than stricter.

## Scoping decisions

1. **1.0 is NOT this milestone's DoD.** M14 clears the two P3 blockers and verifies them; whether
   that suffices for 1.0 is a separate owner call afterwards, evidenced by a re-run device
   checklist. Recorded because the temptation to fold 1.0 in is real and would make the DoD depend
   on a second device run nobody here controls.
2. **#338 surfaces insets to .NET** rather than each shell insetting its own root. Frame parity
   then survives as *"same (layout, insets) → same frames"* instead of *"same numbers"*, which is
   the only option where neither shell has to lie about where `y=0` is.
3. **No ABI change is expected.** Insets are dynamic (rotation, keyboard, cutout), so they cannot
   be an init-time slot; they ride the **existing** `blazornative_host_event(name, payload)`
   export with a generated name — the shape phase 13.5 already found for `themeChanged`. If this
   turns out false it becomes an explicit scoping decision, not a silent one.
4. **#338 is not itself a divergence.** Both shells have *zero* inset handling —
   `grep -rn "WindowInsets|fitsSystemWindows|systemBars" src/BlazorNative.Jni/…` returns 0, and the
   Apple shell's five hits are all comments or tests. They agree perfectly and are identically
   wrong. It is in M14 as a 1.0 blocker and as the consumer that proves 14.0's generator, not as a
   class member.
5. **Every phase is independently shippable** and none blocks a 1.0 cut — carried forward from
   M13 deliberately, because P3's remaining items may clear at any time.

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing — .NET, JVM, **and both device lanes dispatched** (a green *required* set
      does not mean the advisory Android/iOS lanes ran; that is how 11.4's pump bug hid)
- [ ] **The vocabulary sub-shape is closed by generation.** Host-event names emit from
      `src/wire-vocabulary.json` into all three languages. Adding a name by hand to one shell reds.
      #300 closed.
- [ ] **The semantic sub-shape is closed by a pin.** A differential guard asserts the two shells'
      dispatch entry points agree on blocking semantics, and **a NEW divergence reds** — not merely
      the two instances already known. This is the milestone's central claim; if no mechanical form
      exists, that is a finding to record explicitly, never a line to quietly drop.
- [ ] **#339 is fixed, and its invisibility is fixed too.** The deadlock is gone *and* at least one
      of the three seams that made it unreachable in test — `BnAppLifecycle.sinkForTest`'s early
      return, the camera XCTest's `suppressSystemCameraPresentForTest` plus auth overrides, and
      `BnCamera`'s inline-on-main test capture — no longer hides it.
- [ ] **#338 is fixed on BOTH shells.** Insets reported over `host_event`; Android's identical gap
      closed in the same pass. Frame parity re-expressed as *(layout, insets) → frames* and still
      asserted across shells.
- [ ] **The auth semantics agree and are pinned.** One answer to what `requireAuth` means, the
      stored ACL and the read policy pinned against each other, and the **read-side contract**
      covered — a plain get of an auth-bound item is refused with no value leaking. That half was
      never exercised on device because the demo page exposes no plain-get button.
- [ ] **`Debug` and `Verbose` are observable on a real device**, with the method recorded.
- [ ] **The four documentation landmines are fixed**, `$(AppIdentifierPrefix)` explicitly among
      them.
- [ ] **No ABI change** — verified by diffing the export surface, not asserted.

> **No "release tagged in git" criterion.** `docs/planning/CONVENTIONS.md` records **`Milestone
> completion tags a release: no`** — release-please owns the `v<semver>` namespace and Phase 8.6
> retired milestone tags. A checkbox nothing will ever tick is a permanent false gap.

## Phases

1. **Phase 14.0: Pin the host-event vocabulary** — `Surface: Backend`
   - **Goal:** Extend `tools/BlazorNative.WireGen` to emit host-event names into all three
     languages from `src/wire-vocabulary.json`, closing #300 and unblocking 14.2's inset event.
2. **Phase 14.1: The dispatch twins** — `Surface: Backend`
   - **Goal:** Enumerate the runtime's cross-shell dispatch method pairs, restore Kotlin's
     fire-and-forget / and-wait split in Swift, repoint the lifecycle caller, build the
     differential pin, and close at least one test seam that made #339 unreachable.
3. **Phase 14.2: Safe-area insets to .NET** — `Surface: Mixed`
   - **Goal:** Both shells report safe-area insets over `host_event` using 14.0's generated name;
     .NET consumes them; frame parity re-baselined as *(layout, insets) → frames*. Closes #338 on
     iOS and Android together.
4. **Phase 14.3: Auth semantics** — `Surface: Backend`
   - **Goal:** Decide and implement one answer to what `requireAuth` means, pin the stored ACL
     against the read policy, and cover the untested read-side contract. Closes #213 item 1.
5. **Phase 14.4: Device observability, docs, and the device lane** — `Surface: Mixed`
   - **Goal:** Make `Debug` and `Verbose` observable on hardware, land the four documentation
     landmines, and integrate the externally-offered staging script and `ios-arm64` CI lane.
6. **Phase 14.5: Audit and close** — `Surface: Docs`
   - **Goal:** Run `audit-milestone` against the DoD on live evidence and close M14. **No tag** —
     the 8.6 rule, and `CONVENTIONS.md` records `Milestone completion tags a release: no`.

**Ordering rationale.** Exactly one hard dependency: **14.0 → 14.2**, because the inset event needs
a generated name and hand-adding a third name to an unpinned vocabulary is the thing this milestone
exists to stop. 14.1, 14.3 and 14.4 are mutually independent. 14.1 is placed early despite that
independence because it carries the milestone's riskiest claim — the differential pin — and an
early failure there is a cheap re-scope, whereas a late one invalidates the DoD.

## Dependencies on Prior Milestones

- **M13 phase 13.4** established the class and the "mechanical, not instance-by-instance"
  criterion. M14 is its completion, not a new idea.
- **M13 phase 13.5** identified #300 and found that `themeChanged` needs no ABI or .NET change —
  the precedent 14.2's inset event rides.
- **#262 (M12-era)** built `tools/BlazorNative.WireGen` and `src/wire-vocabulary.json`. 14.0
  extends an existing generator rather than writing one.
- **The written C-ABI extension policy** (`website/docs/api-stability.md:151`) governs whether
  anything here may touch the ABI. Current expectation: nothing does.

## External Constraints

- **The device CI lane depends on an external contributor** (@ceesalberts) who offered the staging
  script and lane and is awaiting a preferred shape. It is the only externally-dependent item in
  M14; the log-visibility fix and the documentation fixes in the same phase land regardless.
- **One device, one OS version.** All device evidence is from a single iPhone 17 Pro Max on iOS 26.
  Every fix in M14 is therefore justified by *the code disagreeing with itself*, not by the
  observed symptom — an argument that survives an OS-version change, which "it reproduced on my
  phone" does not.
- **P3 remains open.** Items 4 (APNs), 5 (universal links) and 7 (thermal/background) were not
  completed on the device run. They are out of scope here and keep 1.0 blocked independently of
  M14.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| **The differential pin has no cheap mechanical form** — "assert two shells agree on blocking semantics" is easy to write and may be hard to mechanise | The DoD's central claim degrades into two hand-patches — precisely M13's failure, repeated | 14.1 attempts it **first** and is early enough to re-scope from. A negative result is recorded as a finding with its evidence, never dropped silently |
| **Insets churn every asserted frame table** | Both shells' parity suites re-baseline; a genuine regression hides in the noise | Land 14.2 **after** 14.1 so the tables move once; read the re-baseline as a reviewed diff, the way M13 read its PublicAPI baseline diff as an API review |
| **The auth fix changes behaviour for existing consumers** | An app relying on passcode fallback breaks on upgrade | Pre-1.0, and the surface freezes at 1.0 — this is the cheap window. Ships with a written migration note whichever way the decision goes |
| **14.4 stalls on an external contributor** | The device lane slips | The lane is the only externally-dependent item; split the phase rather than block it |
| **#338 turns out to need an ABI change after all** | Scoping decision 3 is wrong, and M13's frozen-wire property breaks | The extension policy already permits additive growth (appended slots, new ordinals, existing offsets never move). Escalate as an explicit decision, and record it |
| **The class is bigger than three instances** | M14 closes what it knows and the fourth instance ships later | Accepted. The DoD is deliberately written as "a NEW divergence reds", not "these three are fixed" — the mechanism is the deliverable, the instances are its proof |

## Out of Scope

- **APNs and universal links** — the Apple-account cluster on #17. The device run confirms remote
  push is not testable as shipped (no `aps-environment` entitlement) and universal links are not
  implemented (no `associated-domains`).
- **Frame parity for `/layout`, `/scroll`, `/image`** — named in the handover, not compared on
  device for time. Needs hardware.
- **#25 → 1.0 criterion S3** — out of M13 by owner choice; unchanged here.
- **#24 `BlazorNative.Cli`** — the 2026-08-17 audit's verdict stands: it should follow, not lead.
- **A 1.0 cut** — scoping decision 1.

## Open Questions

- **14.3's decision is not pre-made.** Three candidates, all defensible: align the read to the
  stored ACL (`.deviceOwnerAuthenticationWithBiometrics`, matching what `BnBiometrics` already
  uses); relax the ACL to `.userPresence`, which honestly says "biometry or passcode"; or keep the
  behaviour and document that `requireAuth: true` means device-owner authentication, passcode
  included. The hardware result removes the "spurious refusal" argument for the first but does not
  pick between them. **Resolve at the start of 14.3.**
- **What shape should the device CI lane take?** @ceesalberts offered a staging script plus an
  `ios-build`-on-device lane and asked for a preferred shape before opening a PR. He notes
  `IL2072` is exactly 4 on `ios-arm64` — the same constant `ci.yml` already asserts for the
  simulator — so the lane can assert it unchanged, and that `lipo -info` cannot catch a
  wrong-slice stage while `LC_BUILD_VERSION` via `vtool` can. **Needs an answer before 14.4 can
  plan.**
