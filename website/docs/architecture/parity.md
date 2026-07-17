---
id: parity
title: The parity contract
sidebar_label: The parity contract
sidebar_position: 4
---

# The parity contract

**The claim: the same components produce the same frames on Android and iOS — the same
numbers, not merely a similar look.**

That claim is the reason this architecture exists. It is why layout was delegated to one
C++ engine instead of implemented twice, and it is the thing every other decision on this
site is downstream of.

## What it guarantees you

- **Placement is identical.** Every child's computed frame — x, y, width, height — is the
  same on both platforms, because both shells hand the same style names to the same flexbox
  engine and place children where it says.
- **Measurement is identical.** A text leaf is measured by the platform's own text engine,
  but the rules that turn a measurement into a frame are one set of rules. An image's
  measured size is the pixel count of the decoded file read as dp/pt — so a 160px-wide PNG
  measures **160** on both platforms, at every device density.
- **Timing and failure are identical.** When an image lands it causes *one* reflow, never
  two. When it fails, the node keeps measuring zero and reserves nothing, on both
  platforms. When its source changes, the in-flight request is cancelled, on both platforms.

## What it does not guarantee

- **Not pixel-identical rendering.** Fonts, anti-aliasing, ripples, shadows and control
  chrome are the platform's own. A `Button` looks like an Android button and a `UIButton`
  looks like an iOS one. The parity claim is about *geometry*, not about pretending you are
  not on a phone.
- **Not identical internals.** The two shells use different image libraries with different
  cache, eviction and prefetch policies. The contract is drawn on **frames**, deliberately,
  and nowhere else — what must match is the measured size, *when* it is reported, and what
  happens on failure and cancellation.
- **Not "iOS is done".** iOS is simulator-only today.

## The part that matters: it is asserted, not promised

Every earlier statement on this page would be worth nothing if it were maintained by
goodwill. The demo pages' frame tables were once hand-transcribed literals in six device
test files. They matched — *by careful transcription, not by an invariant.* One shell's
`90` becoming `100` while the other's stayed would have left both suites green, both
platforms internally consistent, and this page quietly false.

So there is a drift test. It reads both shells' sources and asserts the frame tables are
equal, in the one required CI lane where Kotlin, Swift and .NET are all visible at once. It
runs on every pull request.

**When this page and that test disagree, the test is right.**

## The normative text

This page states the claim. It does **not** reproduce the contract's rules, because they
have a home and copying them here would create a second one that rots the day the first
moves:

- **The normative contract** —
  [Phase 6.3 design, "The parity contract"](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/plans/2026-07-14-phase-6.3-design.md#the-parity-contract-the-thing-two-libraries-put-at-risk).
  Roughly fifty citations in the shells' own sources treat that section as permanent and
  normative; it is the text both shells are written against.
- **The test that makes it true** —
  [`ShellFrameTableDriftTests.cs`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/tests/BlazorNative.Renderer.Tests/ShellFrameTableDriftTests.cs).
- **The style-table twin** — the same mechanism guards the style routing allow-list across
  three parsers. See [Layout and Yoga](./layout-and-yoga.md).
