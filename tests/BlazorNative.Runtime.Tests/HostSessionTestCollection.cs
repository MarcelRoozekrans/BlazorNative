namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// HostSessionTestCollection
//
// HostSession is a process-wide singleton (static renderer + frame-callback
// slot, plus Phase 3.2's ResetForTests teardown). xUnit serializes tests
// WITHIN a class, but distinct classes run in parallel by default — so
// HostSessionTests and DispatchEventTests (both mutate the singleton) must
// share this named collection to serialize against each other. Declared as
// an explicit marker so the constraint is enforced proactively, not
// retroactively after a confusing race-bug bisect (house style — same
// rationale as the retired WasiBridgeTestCollection).
//
// Phase 3.5: the former "native-shell-bridge" collection merged into this
// one. NavigationTests exercises BOTH process-wide singletons (HostSession
// AND NativeShellBridge/FakeShellHost), and a class can only join ONE
// collection — so every class touching either singleton now serializes here.
// ─────────────────────────────────────────────────────────────────────────────

[CollectionDefinition("host-session")]
public sealed class HostSessionTestCollection
{
    // Marker class — body intentionally empty per xUnit collection-fixture
    // convention. No ICollectionFixture<T> needed; we just want serialization,
    // not a shared fixture instance.
}
