# BlazorNative Analyzer Rules (BN)

The `BlazorNative.Analyzers` project ships the BN rule set — compile-time guards for code
running on the NativeAOT runtime inside a native mobile shell. Phase 4.1 rescoped the set
off the retired WASI premise: two categories remain.

| Category | Meaning | Severity posture |
|---|---|---|
| `BlazorNative.MobilePolicy` | Advisory architecture policy — the API works, but degrades or bypasses the mobile host contract | Warning (BN0013: Error) |
| `BlazorNative.Interop` | Boundary correctness — violations corrupt or abort the C-ABI / bridge contract | Error |

**Escape hatch:** every rule can be suppressed in a reviewed, scoped way — never blanket:

```csharp
#pragma warning disable BN0004 // justification: <why this specific site is safe>
Thread.Sleep(1); // ...
#pragma warning restore BN0004
```

A pragma without a justification comment does not pass review.

---

## BN0004

**Thread.Sleep blocks a runtime thread** — `BlazorNative.MobilePolicy`, Warning.

- **What it flags:** any call to `System.Threading.Thread.Sleep(...)`.
- **Why:** the Kotlin host drives the runtime on a single dispatch lane
  (`BlazorNative-Dispatch`). A blocking sleep on that lane stalls every queued frame and
  event for its full duration — the app freezes, no exception tells you why.
- **Compliant shape:**

  ```csharp
  await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
  ```

- **Escape hatch:** `#pragma warning disable BN0004` with justification (e.g. a
  dev-host-only diagnostic path that never runs on the dispatch lane).

## BN0010

**Raw socket APIs bypass the host bridge** — `BlazorNative.MobilePolicy`, Warning.

- **What it flags:** constructing or invoking anything under `System.Net.Sockets.*`.
- **Why:** network I/O is a host concern — the shell owns permissions, proxies, TLS policy,
  and connection lifecycle. Raw sockets route around all of it and behave differently per
  platform sandbox.
- **Compliant shape:** ride the bridge —

  ```csharp
  var response = await bridge.FetchAsync(new BridgeHttpRequest("https://api.example.com/v1/items"));
  // or, at the DI level:
  services.AddBlazorNativeHttp();   // injects an HttpClient over BridgeHttpHandler
  ```

- **Escape hatch:** `#pragma warning disable BN0010` with justification (e.g. a desktop
  dev-host utility that never ships in the mobile app).

## BN0011

**Parameterless HttpClient bypasses the bridge handler** — `BlazorNative.MobilePolicy`, Warning.

- **What it flags:** `new HttpClient()` — the *parameterless* constructor only. It uses the
  default socket handler, silently bypassing the host bridge.
- **What it does NOT flag:** `new HttpClient(handler)` — constructing over an explicit
  handler stays legal; `BlazorNative.Http` itself constructs over `BridgeHttpHandler`.
- **Compliant shape:** inject the client registered by `AddBlazorNativeHttp()`:

  ```csharp
  public sealed class ItemsService(HttpClient http)   // DI-injected, bridge-backed
  {
      public Task<string> GetAsync() => http.GetStringAsync("/v1/items");
  }
  ```

- **Escape hatch:** `#pragma warning disable BN0011` with justification.

## BN0013

**Process APIs are unsupported in the app sandbox** — `BlazorNative.MobilePolicy`, **Error**.

- **What it flags:** constructing or invoking anything under `System.Diagnostics.Process*`
  (including `ProcessStartInfo`).
- **Why:** Android app sandboxes give a BlazorNative app no process-spawning surface —
  this is a correctness error, not advice. There is no compliant alternative; process
  management belongs to the native shell, if anywhere.
- **Escape hatch:** `#pragma warning disable BN0013` with justification (expected: none).

## BN0014

**Bridge event handlers must complete synchronously** — `BlazorNative.Interop`, **Error**.

- **What it flags:** registering an `async` lambda or an `async` method against
  `IMobileBridge.NativeEvents` (`bridge.NativeEvents += async e => ...`).
- **Why:** handlers run synchronously inside a native callback window (`DevHostBridge`'s
  mock multicast is sync; the production lane is the single-threaded dispatch lane).
  `Action<NativeEvent>`
  forces an async lambda to compile as `async void` — fire-and-forget: the continuation
  escapes the callback window and exceptions vanish.
- **Compliant shape:**

  ```csharp
  bridge.NativeEvents += e =>
  {
      // synchronous work only; defer async work explicitly:
      _ = Dispatcher.InvokeAsync(() => HandleAsync(e));
  };
  ```

- **Note:** `NativeEvents`' own redesign is a ledgered open item (`NativeShellBridge`
  currently stubs it no-op); the rule guards the surviving contract.
- **Escape hatch:** `#pragma warning disable BN0014` with justification (expected: none).

## BN0020

**Exceptions must not escape `[UnmanagedCallersOnly]` exports** — `BlazorNative.Interop`, **Error**.

- **What it flags:** a `[UnmanagedCallersOnly]` method whose body is not a single top-level
  `try` with a catch-all clause. A catch-all is a bare `catch` or an unfiltered
  `catch (Exception)`; a `throw` (rethrow or fresh) in **any** catch clause of the
  top-level try fires the rule. Expression-bodied exports are always flagged.
- **Why:** an exception crossing the C-ABI boundary is undefined behavior under NativeAOT —
  on Android it aborts the process with no managed diagnostics. Every export must convert
  failure into a return code (the rc-code contract `Exports.cs` follows).
- **Compliant shape (the `Exports.cs` reference pattern):**

  ```csharp
  [UnmanagedCallersOnly(EntryPoint = "blazornative_mount", CallConvs = new[] { typeof(CallConvCdecl) })]
  public static int Mount(IntPtr nameUtf8)
  {
      try
      {
          // entire body inside the try — no statements outside it
          return HostSession.TryMount(nameUtf8);
      }
      catch (Exception ex)
      {
          Console.Error.WriteLine($"[Exports] mount failed: {ex}");
          return 2;
      }
  }
  ```

- **Heuristic scope (deliberate):** the rule is syntactic and strict — it demands the
  top-level try/catch shape rather than proving exhaustively that no exception escapes.
  A trivially-safe body (e.g. `=> s_versionString;`) is still flagged: wrap it. A `throw`
  in *any* catch clause of the top-level try fires the rule — a rethrow from a specific
  catch (`catch (IOException) { throw; }`) escapes even when a catch-all sibling exists.
  Known blind spots: a `throw` inside a *nested* try within a catch clause (which that
  nested try itself catches) is conservatively treated as an escape; conversely, a
  `finally` block that throws is **not** analyzed and can still leak an exception across
  the boundary — do not throw from `finally` in an export.
- **Escape hatch:** `#pragma warning disable BN0020` with justification (expected: none —
  wrap instead).

## BN0021

**`[UnmanagedCallersOnly]` exports must pin `EntryPoint` and `CallConvCdecl`** — `BlazorNative.Interop`, **Error**.

- **What it flags:** a `[UnmanagedCallersOnly]` method missing `EntryPoint = "..."`, or
  missing `CallConvs = new[] { typeof(CallConvCdecl) }` (or whose `CallConvs` does not
  include `CallConvCdecl`).
- **Why:** the JNA host binds to exports **by name** and assumes **cdecl**. An unnamed
  export binds by mangled managed name; an implicit calling convention rides platform
  defaults. Both are silent ABI drift vectors — they work until a toolchain or platform
  change breaks them without a compile error. (Static + blittable-signature requirements
  are already compiler-enforced — CS8894 family — and need no analyzer.)
- **Compliant shape:**

  ```csharp
  [UnmanagedCallersOnly(EntryPoint = "blazornative_version", CallConvs = new[] { typeof(CallConvCdecl) })]
  ```

- **Escape hatch:** `#pragma warning disable BN0021` with justification (expected: none).

---

## Retired rule IDs

Retired in Phase 4.1 (`AnalyzerReleases.Shipped.md`, release `4.1.0`). The WASI-era
premise ("this API throws on WASI Preview 1") died with the Mono-WASI runtime; NativeAOT
has real threads, a real thread pool, real file I/O. **IDs are never reused.**

| ID | Was | Why retired |
|---|---|---|
| BN0001 | `new Thread(...)` forbidden | NativeAOT has real threads |
| BN0002 | `Task.Run(...)` forbidden | Thread pool exists; offloading is legal |
| BN0003 | `Parallel.*` forbidden | Multi-core parallelism works |
| BN0005 | Sync primitives (Mutex/Monitor/…) flagged | Locks are meaningful again |
| BN0006 | `[ThreadStatic]` flagged | Now actively FALSE — `FrameArena` uses `[ThreadStatic]` deliberately |
| BN0012 | `System.IO.File/Directory/Path` flagged | File I/O works in the app sandbox |
