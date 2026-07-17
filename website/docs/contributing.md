---
id: contributing
title: Contributing
sidebar_label: Contributing
sidebar_position: 7
---

# Contributing

Contributor material lives **in the repository**, next to the things it describes, and this
page points at it rather than reproducing it. That is not laziness — it is the rule this
whole site is built on: a fact copied to a second place rots the day the first one moves,
and "we'll keep it updated" has never once been a mechanism.

## Start here

| | Where |
|---|---|
| **The repository** | [MarcelRoozekrans/BlazorNative](https://github.com/MarcelRoozekrans/BlazorNative) |
| **The README** — status, prerequisites, the test surface, the project layout | [README.md](https://github.com/MarcelRoozekrans/BlazorNative#readme) |
| **Issues** | [github.com/MarcelRoozekrans/BlazorNative/issues](https://github.com/MarcelRoozekrans/BlazorNative/issues) |
| **CI — every count this project claims** | [ci.yml](https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml) |

## The documents worth knowing about

- **[`docs/bridge-extension.md`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/bridge-extension.md)**
  — how to add a capability to the host bridge. It touches the C-ABI in several places at
  once, and this is the procedure that names all of them. Its declared audience is a future
  contributor, which is exactly why it stays in the repository.
- **[`docs/planning/ROADMAP.md`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/planning/ROADMAP.md)**
  and
  **[`docs/planning/MILESTONE.md`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/planning/MILESTONE.md)**
  — what is done, what is next, and why. If you want this project's status, read these, not
  this site.
- **[`docs/plans/`](https://github.com/MarcelRoozekrans/BlazorNative/tree/main/docs/plans)**
  — a dated, write-once design record: one design, one plan and one conclusion per phase,
  with the measurements that drove each decision and the things that were deliberately not
  done. It is the answer to almost every "why is it like this?" question, and it is an
  append-only log, which is why it is linked as a whole rather than published here.
- **[`docs/GITHUB-SETUP.md`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/GITHUB-SETUP.md)**
  — repository machinery: branch protection, required checks, secrets. Owner-facing.

## What a change is held to

Three compile gates are required on every pull request — one per surface (.NET, Android,
iOS). They are the contract, and they are worth understanding before you open a PR:

- **The counts are asserted, not observed.** A test-count drift fails the build. If your
  change adds tests, the number moves *deliberately*, in the same commit.
- **Cross-shell facts are pinned by drift tests.** The style routing allow-list, the demo
  frame tables and the shells' shared literals are parsed out of Kotlin, Swift and C# and
  asserted equal, because a fact that lives in three languages drifts silently in all three.
- **The zero-warning bar applies**, including to the analyzers' own rules.

If a gate reds on something that looks unrelated to your change, it is usually one of the
drift pins telling you that a fact you moved has a second home you did not know about. That
is the pin doing its job.

## This site

The site is a Docusaurus project in
[`website/`](https://github.com/MarcelRoozekrans/BlazorNative/tree/main/website). Two things
about it are worth knowing if you edit it:

- **The component reference is generated** from `BlazorNative.Components`' XML docs at build
  time and is not committed. To fix what it says, edit the `///` comment on the member — the
  page is a printout, not a source.
- **A dead internal link fails the build.** That is deliberate.
