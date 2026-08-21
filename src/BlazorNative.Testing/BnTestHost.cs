using System.Diagnostics.CodeAnalysis;
using BlazorNative.Renderer;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Testing;

// ─────────────────────────────────────────────────────────────────────────────
// BnTestHost — the supported way to mount a page in a test (#25).
//
// WHY THIS EXISTS AT ALL. Until now the only way to test a BlazorNative page was
// to resolve `NativeRenderer` and subscribe to its `Frames` event — and
// `NativeRenderer` is tier NOT-API, `[EditorBrowsable(Never)]`, with 1.0
// criterion S3 saying it should become `internal`. So consumers were told in one
// breath to test their pages and not to touch the only thing that lets them.
// This type is what makes S3 executable: once it ships, the renderer can go
// internal without removing anybody's ability to test.
//
// WHAT IT DELIBERATELY HIDES. The renderer, the dispatcher, the frame plumbing
// and — most importantly — the patch model. See BnTestTree for why the projection
// is the contract and the patches are not.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Mounts a component in-process and exposes what it rendered.</summary>
/// <example>
/// <code>
/// using var host = BnTestHost.Mount&lt;MyPage&gt;();
///
/// Assert.Equal("view", host.Tree.Root.NodeType);
/// Assert.Equal("Hello", host.Tree.FindText("Hello").Text);
///
/// await host.ClickAsync(host.Tree.FindAll("button")[0]);
/// Assert.Equal("Tapped", host.Tree.FindAll("text")[0].Text);
/// </code>
/// </example>
/// <remarks>
/// <para><b>No shell, no device.</b> This runs the real renderer and the real
/// components; it does not run Yoga, so it answers "what did my page render?" and
/// never "how big is it?". Layout is asserted by the framework's own cross-platform
/// frame tests, which need two real shells.</para>
/// <para><b>Not thread-safe</b>, deliberately: the renderer is single-threaded by
/// contract, and a harness that pretended otherwise would let a test pass that a
/// real app could not.</para>
/// </remarks>
public sealed class BnTestHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly NativeRenderer _renderer;
    private readonly BnTestTree _tree;
    private int _frames;

    private BnTestHost(ServiceProvider provider, NativeRenderer renderer, BnShell shell)
    {
        _provider = provider;
        _renderer = renderer;
        _tree = new BnTestTree(shell);
    }

    /// <summary>The widget tree as it stands after every frame so far.</summary>
    public BnTestTree Tree => _tree;

    /// <summary>How many frames the renderer has produced.</summary>
    /// <remarks>Useful for a smoke check that anything rendered at all. <b>Do not
    /// assert an exact count</b> for a given interaction: how many frames a change
    /// takes is renderer-internal, so pinning it would make an optimisation a
    /// breaking change for you.</remarks>
    public int FrameCount => _frames;

    /// <summary>Mounts <typeparamref name="TComponent"/> and returns the host.</summary>
    /// <typeparam name="TComponent">The component to mount — usually a page.</typeparam>
    /// <param name="parameters">Parameters to supply, or null for none.</param>
    /// <param name="configureServices">Adds to the DI container the component is
    /// rendered against — register your own services, or a
    /// <c>DevHostBridge</c> when the page injects <c>IMobileBridge</c>.</param>
    /// <param name="shell">Which shell's projection to model. The shells agree on the
    /// wire and differ in how they project it — see <see cref="BnShell"/>. Defaults to
    /// <see cref="BnShell.Ios"/>, which is what the harness modelled before it could
    /// name a shell at all.</param>
    public static BnTestHost Mount<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        IDictionary<string, object?>? parameters = null,
        Action<IServiceCollection>? configureServices = null,
        BnShell shell = BnShell.Ios)
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddBlazorNativeRenderer();
        configureServices?.Invoke(services);

        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            var renderer = provider.GetRequiredService<NativeRenderer>();

            // STRICT, and not negotiable for a test host: the renderer's production
            // posture is log-and-continue, which is right for a running app and wrong
            // for a test — a swallowed render fault would make a green test that
            // rendered nothing. Strict mode surfaces it at the mount call.
            renderer.StrictErrors = true;

            var host = new BnTestHost(provider, renderer, shell);
            renderer.Frames += host.OnFrame;

            ParameterView view = parameters is null
                ? ParameterView.Empty
                : ParameterView.FromDictionary(parameters);

            // Mount is synchronous under the renderer's inline dispatcher (the
            // sync-mount contract the C-ABI host depends on), so by the time this
            // returns the tree is populated — no awaiting, no polling for a shape.
            renderer.Mount<TComponent>(view);
            return host;
        }
        catch
        {
            // #281: Mount is the path StrictErrors = true exists to produce a throw
            // on. Everything above is already built by then, so without this the
            // provider and the renderer it owns leak on exactly the case the harness
            // is designed for — and a test suite full of expected-throw mounts leaks
            // one container per assertion.
            provider.Dispose();
            throw;
        }
    }

    private ValueTask OnFrame(RenderFrame frame, CancellationToken ct)
    {
        _frames++;
        _tree.Apply(frame);
        return ValueTask.CompletedTask;
    }

    /// <summary>Clicks a node — the <c>click</c> event a <c>BnButton</c> attaches.</summary>
    /// <param name="node">The node to click, from <see cref="Tree"/>.</param>
    /// <exception cref="InvalidOperationException">If the node has no click handler,
    /// which is nearly always the actual bug: the component did not attach one
    /// (a <c>BnButton</c> with no <c>OnClick</c> emits no attach at all).</exception>
    public Task ClickAsync(BnTestNode node) => DispatchAsync(node, "click", null);

    /// <summary>Raises a <c>change</c> with <paramref name="value"/> — what typing
    /// into a <c>BnInput</c>, or toggling a <c>BnCheckbox</c>, sends.</summary>
    /// <param name="node">The node to change, from <see cref="Tree"/>.</param>
    /// <param name="value">The payload, in the wire's grammar: the text for an
    /// input, exactly <c>"true"</c>/<c>"false"</c> for a checkbox or switch, an
    /// invariant number for a slider.</param>
    public Task ChangeAsync(BnTestNode node, string value) => DispatchAsync(node, "change", value);

    /// <summary>Raises an arbitrary event by name, for anything the two helpers
    /// above do not cover (<c>focus</c>, <c>blur</c>, <c>scroll</c>).</summary>
    /// <param name="node">The node to dispatch on, from <see cref="Tree"/>.</param>
    /// <param name="eventName">The wire event name, without the <c>on</c> prefix.</param>
    /// <param name="payload">The event payload, or null.</param>
    public async Task DispatchAsync(BnTestNode node, string eventName, string? payload)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrEmpty(eventName);

        if (!node._handlers.TryGetValue(eventName, out int handlerId))
            throw new InvalidOperationException(
                $"{node} has no '{eventName}' handler attached. The component did not wire one — "
                + $"a BnButton with no OnClick emits no attach at all, so there is nothing to "
                + $"dispatch to. Attached here: "
                + (node.Events.Count == 0 ? "(none)" : string.Join(", ", node.Events)));

        await _renderer.DispatchUiEventAsync(new NativeUiEvent(node.Id, handlerId, eventName, payload));
    }

    /// <summary>Disposes the renderer and the container it was built on.</summary>
    public void Dispose()
    {
        _renderer.Frames -= OnFrame;
        _renderer.Dispose();
        _provider.Dispose();
    }
}
