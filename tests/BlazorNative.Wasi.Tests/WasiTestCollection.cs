using Xunit;

namespace BlazorNative.Wasi.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// WasiTestCollection
//
// Single shared WasiPublishFixture for all wasi tests. Without this, xUnit's
// per-class IClassFixture would instantiate the fixture twice — once for
// BootSmoke, once for ExportSmoke — and run them in parallel. Both fixtures
// then race to delete + re-publish the same bin/Release/.../AppBundle dir,
// usually producing IOException ("file in use by another process") on the
// loser.
//
// With [Collection("Wasi")] on both test classes, xUnit:
//   1. Shares one fixture instance across the collection (single publish)
//   2. Runs tests in the collection sequentially (no race)
// ─────────────────────────────────────────────────────────────────────────────

[CollectionDefinition("Wasi")]
public sealed class WasiTestCollection : ICollectionFixture<WasiPublishFixture>
{
    // Marker class — body intentionally empty per xUnit collection-fixture convention.
}
