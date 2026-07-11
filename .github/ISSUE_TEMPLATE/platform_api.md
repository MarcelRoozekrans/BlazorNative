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
- **iOS (Swift shell, M5):** feasibility notes — iOS lands in Milestone 5, so
  Android-first proposals are fine

## Fallback behavior

What happens on a platform that doesn't support this API, and in the JVM dev
loop?
