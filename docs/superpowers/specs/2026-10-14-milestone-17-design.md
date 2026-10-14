# Milestone 17 Design — `Close the Dispatch Lane's Debt`

**Date:** 2026-10-14
**Milestone:** `17`
**Stage:** milestone (one milestone, multiple phases)

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
- [ ] **#425 is fixed, by session generation, or by the fallback if no carrier exists.**
  - A frame from a retired session never reaches the new one. A pin delivers a late frame after
    `retire()` and asserts this.
  - **Fallback, owner decision 2026-10-14:** if 17.3's design proves that no existing carrier can
    hold a generation without breaking the frozen ABI, `retire()` closes .NET's frame gate for the
    old session instead, as `shutdown()` already does.
  - **Recreation is wired:** the reference shell and the template retire the old runtime and start
    a new one on Activity recreation, proven on the Android device lane by a rotation test.
  - **Navigation history resets with the session,** per #425's comment: after recreation, Back at
    the root finishes and does not navigate to the old sub-page. This is pinned on the same path.
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

## Phases

1. **Phase 17.1: Fault attribution (#464, #494)** — `Surface: Backend`
   - **Goal:** A fault belongs to the dispatch whose continuation raised it. 16.7's
     `FaultNoticeTests` partials are proven in the same files.
2. **Phase 17.2: Android host-call contract (#440)** — `Surface: Backend`
   - **Goal:** Android's biometrics and KeyStore work leaves `hostCallBegin` for a background
     executor, mirroring iOS 16.4.
3. **Phase 17.3: Activity recreation (#425)** — `Surface: Backend`
   - **Goal:** A recreated Activity retires the old runtime and starts a fresh one. It sees no
     frame from the old session, carried by a session generation or, failing that, by `retire()`
     closing .NET's gate. Its navigation history starts empty.
4. **Phase 17.4: Fault-path parity (#426, #435)** — `Surface: Backend`
   - **Goal:** Android redacts like iOS, and the slow-handler warning names the app's handler
     through a forwarding hook.
5. **Phase 17.5: Test-infrastructure honesty (#454, #424, #458)** — `Surface: Infra`
   - **Goal:** No local run can hang silently, test a stale native library, or fail with a bare
     message.
6. **Phase 17.6: Audit and close** — `Surface: Docs`
   - **Goal:** Run `audit-milestone` against this DoD on live evidence. **No tag**, per
     `CONVENTIONS.md`.

**Order.**
- 17.1 goes first because it changes the fault path that 17.4 then redacts and names.
- 17.2 and 17.3 are independent of each other, and of 17.1 except through the shared lanes.
- 17.5 can move earlier if #454 or #424 bites during 17.1 to 17.4.

## Dependencies on Prior Milestones

- **M16:** the render thread, the dispatch scope with its flowing `AsyncLocal` and its
  thread-bound mark, the FaultNotice and BackState ops, and the rc contract in three pinned copies.
  M17 changes the classification rules, not the mechanism.
- **The 16.8 re-audit's lessons:** a pin's register cell is written by the phase that adds it,
  and anchors get reds too.
- **Setup:** `setup.sh` (#490) makes the .NET suite, the bionic publish and the reflection-only JVM
  pins runnable on Linux. JNA-loading JVM tests still need Windows CI (#491).

## External Constraints

- **The ABI is frozen** (1.0 criterion A): 80-byte struct, 10 exports. #425's session generation
  has to ride something that already exists.
- **Device lanes** run one at a time per ref. An iOS run is about 25 minutes and an Android run
  about 30.
- **`ci.yml` has no `workflow_dispatch`,** so a Windows-only JVM mutation needs a throwaway draft PR.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| #425's generation cannot ride the frozen ABI or the existing frame format without a breaking change | 17.3 needs a version bump or a different fix | 17.3's design proves the carrier first, before any code: the frame header, a patch field, or the `register_frame_callback` context. If none works, stop and bring the owner the trade-off rather than breaking the ABI |
| #464's flowing-scope check changes which rc a dispatch returns: B goes from rc 0 to rc 2 in #464's scenario | An app that relied on today's misattribution sees a different rc | Ship as `fix!` with a BREAKING note, as 16.7 did; RcContractCopiesTests keeps the three contract copies in step |
| Moving Android KeyStore work off the render thread (#440) introduces ordering races between `hostCallBegin` and completion | A completion before its begin returns, as 16.4 saw on iOS | Reuse iOS 16.4's design and its pins as the template, and keep `hostCallBegin`'s record-then-enqueue order |
| The forwarding hook (#435) widens the components' internal surface | A new seam that the NOT-API tiering must cover | Keep it `internal`/EditorBrowsable(Never), and check it against `api-tiers` and `PublicAPI` baselines |
| Pins added without register cells, again | The next audit repeats 16.8's finding | DoD item: cells are written in the phase that adds the pin |

## Open Questions

None. The owner decided on 2026-10-14:
- the theme;
- the scope (#464, #494, #440, #425, #426, #435, #454, #424, #458);
- #464, the flowing scope;
- #425: the session generation, with `retire()` closing the gate as the fallback; recreation
  wired into the shell and the template; navigation history reset in the same phase;
- #435, the forwarding hook;
- #426, redact like iOS.
