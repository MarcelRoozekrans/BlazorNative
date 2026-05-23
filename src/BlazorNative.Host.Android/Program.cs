using BlazorNative.Core;
using BlazorNative.DevHost;

// ─────────────────────────────────────────────────────────────────────────────
// BlazorNative DevHost
// Runs your Blazor app in a normal browser with a mock native bridge.
// Gives you hot reload, debugging, and DevTools — no WASM compile needed.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Register the dev bridge as the IMobileBridge implementation
var devBridge = new DevHostBridge();
builder.Services.AddSingleton<IMobileBridge>(devBridge);
builder.Services.AddSingleton(devBridge); // also register concrete type for DevTools

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorNative.Blazor._Imports).Assembly);

// ── DevTools API ─────────────────────────────────────────────────────────────

var devTools = app.MapGroup("/dev").WithTags("DevTools");

// POST /dev/event  — inject a native event from Postman/curl/DevTools UI
devTools.MapPost("/event", (NativeEventDto dto, DevHostBridge bridge) =>
{
    bridge.InjectEvent(dto.Name, dto.Payload);
    return Results.Ok(new { injected = true, name = dto.Name });
});

// GET /dev/storage — inspect current storage state
devTools.MapGet("/storage", (DevHostBridge bridge) =>
    Results.Ok(bridge.StorageSnapshot));

// GET /dev/routes — inspect navigation history
devTools.MapGet("/routes", (DevHostBridge bridge) =>
    Results.Ok(new { current = bridge.RouteHistory.LastOrDefault("/"), history = bridge.RouteHistory }));

// DELETE /dev/storage/{key} — clear a storage key
devTools.MapDelete("/storage/{key}", (string key, DevHostBridge bridge) =>
{
    bridge.StorageSnapshot.TryGetValue(key, out _); // just for logging
    return Results.Ok(new { deleted = key });
});

app.Run();

record NativeEventDto(string Name, string? Payload = null);
