using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// WasiBridgeTestCollection
//
// WasiBridge.Current is a static singleton — the ctor sets it, Dispose nulls
// it. xUnit serializes tests within a single class, so BridgeEventTests alone
// is safe today. The moment any other test class instantiates `new WasiBridge()`
// the default cross-class parallelization would race. This collection marker
// declares the singleton constraint at the test layer so it's enforced
// proactively, not retroactively after a confusing race-bug bisect.
// ─────────────────────────────────────────────────────────────────────────────

[CollectionDefinition("WasiBridge")]
public sealed class WasiBridgeTestCollection
{
    // Marker class — body intentionally empty per xUnit collection-fixture
    // convention. No ICollectionFixture<T> needed; we just want serialization,
    // not a shared fixture instance.
}
