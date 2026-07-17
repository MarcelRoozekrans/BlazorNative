# BlazorNative.Device

The [BlazorNative](https://marcelroozekrans.github.io/BlazorNative/) device APIs:
DI-injectable, ergonomic facades over the hosting native shell's permission-gated
host-call bridge, so Blazor components use device capabilities on NativeAOT with
**denial delivered as data — never an exception, never a hang**.

## Geolocation

```csharp
// DI (the runtime composition root wires this for you):
services.AddBlazorNativeDevice();

// In a component — inject the facade, not the low-level bridge:
[Inject] public IGeolocation Geo { get; set; } = default!;

async Task LocateAsync()
{
    var result = await Geo.GetCurrentPositionAsync();
    if (result.Status == GeolocationStatus.Granted && result.Position is { } p)
        _text = $"{p.Latitude}, {p.Longitude}";
    else
        _text = result.Status.ToString(); // Denied / DeniedPermanently / Restricted / ...
}
```

A single `GetCurrentPositionAsync` runs the whole permission dance host-side
(check → prompt → obtain-a-fix / note-a-denial) and always resolves with a status
value. `CheckPermissionAsync` reads the current status **without** prompting, for a
UI that wants to show state before offering a "use my location" action.

Part of the BlazorNative framework. See the
[documentation](https://marcelroozekrans.github.io/BlazorNative/) for the full story.
