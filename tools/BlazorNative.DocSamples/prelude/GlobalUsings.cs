// ─────────────────────────────────────────────────────────────────────────────
// GlobalUsings.cs — Phase 15.5 (#291). The C# usings every generated docs-sample
// file/statements source compiles against.
//
// Kept to global using lines only, per spec risk 2: a sample that needs more
// scaffolding than a using is fixed in the PAGE instead of here.
//
// System.Net.Http and Microsoft.Extensions.DependencyInjection are not part of
// Sdk.Razor's implicit-usings set for a non-web project (that shortlist is
// System/System.Collections.Generic/System.Linq/System.Threading/
// System.Threading.Tasks only) but both appear in real docs samples
// (HttpClient in guides/rest-backends.md, AddSingleton in guides/state.md).
// ─────────────────────────────────────────────────────────────────────────────

global using System.Net.Http;
global using Microsoft.Extensions.DependencyInjection;
