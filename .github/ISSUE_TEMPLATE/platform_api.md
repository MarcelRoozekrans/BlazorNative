---
name: Platform API proposal
about: Propose a new native platform API (camera, GPS, clipboard, ...)
title: 'Platform API: '
labels: ''
assignees: ''
---

## API

Which platform capability should BlazorNative expose (e.g. geolocation, camera,
haptics)?

## .NET surface

What should app code see? Sketch the interface (method signatures on a service,
or an extension of `IMobileBridge`).

## Bridge surface

Platform APIs flow through the host-registered C-ABI callback struct
(`blazornative_register_bridge`, established in Phase 3.1). Describe:

- Sync or async? (async operations use the completion pattern —
  begin-call returns immediately, the host answers through a completion export)
- Payload shape across the boundary (UTF-8 buffers, retry protocol for
  oversized responses)

## Platform implementations

- **Android (Kotlin shell):** which platform API backs it, permissions needed
- **iOS (Swift/UIKit shell — shipped in M5):** which platform API backs it,
  permissions needed. The shell exists and runs on the **simulator** (exercised
  on CI macOS runners), so iOS proposals are actionable today. Note that
  anything needing **physical iOS hardware** (or App Store validation) is gated
  on an Apple Developer account and lands in **M9** — Android-first proposals
  are still fine.

## Fallback behavior

What happens on a platform that doesn't support this API, and in the JVM dev
loop?
