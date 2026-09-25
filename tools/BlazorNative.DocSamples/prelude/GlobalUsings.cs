// ─────────────────────────────────────────────────────────────────────────────
// GlobalUsings.cs — Phase 15.5 (#291). The C# usings every generated docs-sample
// file/statements source compiles against.
//
// Kept to global using lines only, per spec risk 2: a sample that needs more
// scaffolding than a using is fixed in the PAGE instead of here.
//
// System.Net.Http is NOT listed here (fix round 1 correction) — Sdk.Razor's own
// implicit-usings set already includes it, the same as plain Sdk.Razor's shortlist
// covers System/System.Collections.Generic/System.Linq/System.Threading/
// System.Threading.Tasks/System.Net.Http. A redundant global using here would risk
// masking that fact rather than testing it. Microsoft.Extensions.DependencyInjection
// is NOT implicit, and IS needed (AddSingleton in guides/state.md).
// ─────────────────────────────────────────────────────────────────────────────

global using Microsoft.Extensions.DependencyInjection;
