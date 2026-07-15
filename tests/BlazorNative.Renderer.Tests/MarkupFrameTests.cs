using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// MarkupFrameTests — Phase 7.0 (the Razor-compilation spike).
//
// The Razor compiler preserves whitespace-only text BETWEEN sibling elements as
// Markup frames (`__builder.AddMarkupContent(n, "\n    ")`) — .NET 5+ whitespace
// trimming only removes it leading/trailing within an element and around C#
// blocks. Hand-written BuildRenderTree never emitted Markup frames, so until
// 7.0 the walk's fall-through silently dropped them WITHOUT a sibling slot —
// and Blazor's diff SiblingIndex counts markup frames as siblings. Any edit
// addressing a sibling AFTER a markup frame (the echo span in every .razor
// page) would resolve to the wrong slot: a poisoned cursor at best, the wrong
// node's text replaced at worst. Armed the moment the first .razor compiles.
//
// The contract pinned here:
//   • a whitespace-only Markup frame occupies a sibling SLOT (diff indices
//     stay aligned) but contributes NO patch and NO host view (insert-index
//     translation skips it) — native widget trees have no whitespace nodes;
//   • a NON-whitespace Markup frame is a contract violation (native has no
//     innerHTML): strict throws, non-strict logs-and-drops — but it still
//     occupies its slot either way, so later indices don't shift.
//
// Harness: same patterns as RegionWalkTests (strict mode, Frames capture,
// dispatch-driven re-renders).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MarkupFrameTests
{
    private static (NativeRenderer Renderer, List<RenderFrame> Frames) BuildRenderer(bool strict = true)
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = strict;
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        return (renderer, frames);
    }

    /// <summary>The exact shape the Razor compiler emits for
    /// <c>&lt;div&gt;&lt;input …/&gt; &lt;span&gt;@_text&lt;/span&gt;&lt;/div&gt;</c>:
    /// whitespace Markup frames between the element siblings. The span (and
    /// its text) sit AFTER markup siblings — the diff-index alignment case.</summary>
    private sealed class MarkupBetweenSiblings : ComponentBase
    {
        private string _text = "start";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.OpenElement(1, "input");
            b.AddAttribute(2, "onchange",
                EventCallback.Factory.Create<ChangeEventArgs>(this, e => _text = e.Value?.ToString() ?? ""));
            b.CloseElement();
            b.AddMarkupContent(3, "\n    ");
            b.OpenElement(4, "span");
            b.AddContent(5, _text);
            b.CloseElement();
            b.AddMarkupContent(6, "\n");
            b.CloseElement();
        }
    }

    [Fact]
    public async Task WhitespaceMarkup_EmitsNoPatch_AndNoNode()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<MarkupBetweenSiblings>(ParameterView.Empty);
        Assert.NotEmpty(frames);
        var mount = frames[0];

        // Exactly div + input + span + the span's text node — no whitespace
        // "text" node ever reaches the wire.
        Assert.Equal(4, mount.Patches.OfType<CreateNodePatch>().Count());
        Assert.DoesNotContain(mount.Patches.OfType<ReplaceTextPatch>(),
            p => string.IsNullOrWhiteSpace(p.Text));

        // The span still parents under the div, unmoved by the markup sibling.
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "start");
        var span = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == Assert.IsType<int>(
                Assert.Single(mount.Patches.OfType<CreateNodePatch>(), c => c.NodeId == text.NodeId).ParentId));
        var div = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal(div.NodeId, span.ParentId);
    }

    /// <summary>THE regression this file exists for: an UpdateText edit
    /// addressing the span AFTER a markup sibling. Blazor's StepIn counts the
    /// markup frame (input 0, markup 1, span 2); if the walk dropped markup
    /// without a slot, GetSlotAt(2) misses and the cursor poisons. Note HOW
    /// the test bites: a poisoned cursor makes the UpdateText miss silently
    /// (no patch, no strict throw — a failed StepIn is not a violation), so
    /// it is the Assert.Single below finding NO ReplaceText that reddens,
    /// not an exception.</summary>
    [Fact]
    public async Task UpdateText_AfterMarkupSibling_ResolvesTheRightNode()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<MarkupBetweenSiblings>(ParameterView.Empty);
        var mount = frames[0];
        var textNode = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "start").NodeId;
        var attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "change");

        await renderer.DispatchUiEventAsync(
            new NativeUiEvent(0, attach.HandlerId, "change", "typed"));

        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var replaced = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>());
        Assert.Equal(textNode, replaced.NodeId);
        Assert.Equal("typed", replaced.Text);
    }

    /// <summary>Mid-list INSERT after a markup sibling: the markup slot must
    /// contribute ZERO host views to the insert-index translation — the new
    /// element lands between its real-view neighbours, not one off.</summary>
    private sealed class ConditionalAfterMarkup : ComponentBase
    {
        private bool _show;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.OpenElement(1, "button");
            b.AddAttribute(2, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _show = true));
            b.CloseElement();
            b.AddMarkupContent(3, "\n    ");
            if (_show)
            {
                b.OpenElement(4, "span");
                b.AddContent(5, "inserted");
                b.CloseElement();
            }
            b.OpenElement(6, "input");
            b.CloseElement();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task Insert_AfterMarkupSibling_HostIndexSkipsTheMarkup()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<ConditionalAfterMarkup>(ParameterView.Empty);
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>(), p => p.EventName == "click");

        await renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");

        // The span arrives at Blazor sibling 2 (button 0, markup 1) — but the
        // HOST insert index is 1: button(0) · span(1) · input(2). A markup
        // slot counted as a view would push it to 2 (after the input).
        var div = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        var inserted = Assert.Single(frames[^1].Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "text" && p.ParentId == div.NodeId);
        Assert.Equal(1, inserted.InsertIndex);
    }

    // ── Markup count CHANGES across renders (review F3) ───────────────────────
    //
    // Real .razor @if blocks carry their own leading/trailing whitespace, so
    // 7.1 hits BOTH changing-markup-count diff arms on page one: PrependFrame
    // whose reference frame is a Markup frame (toggle ON) and RemoveFrame of a
    // Markup slot (toggle OFF). Until these tests, both arms were implemented
    // but never executed.

    /// <summary>The markup lives INSIDE the conditional — exactly what the
    /// Razor compiler emits for
    /// <c>@if (x) {\n    &lt;span&gt;…&lt;/span&gt;\n}</c>. A tail span AFTER
    /// the conditional gets a new text on every click, so each toggle's diff
    /// also carries an edit that only resolves if the slot list tracked the
    /// markup count.</summary>
    private sealed class MarkupInsideConditional : ComponentBase
    {
        private bool _show;
        private int _clicks;
        private string _tail = "tail-0";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.OpenElement(1, "button");
            b.AddAttribute(2, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () =>
                {
                    _clicks++;
                    _show = !_show;
                    _tail = $"tail-{_clicks}";
                }));
            b.CloseElement();
            if (_show)
            {
                b.AddMarkupContent(3, "\n    ");
                b.OpenElement(4, "span");
                b.AddContent(5, "inserted");
                b.CloseElement();
                b.AddMarkupContent(6, "\n");
            }
            b.OpenElement(7, "span");
            b.AddContent(8, _tail);
            b.CloseElement();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task ConditionalMarkup_ToggleBothWays_SlotsTrackTheMarkupCount()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<MarkupInsideConditional>(ParameterView.Empty);
        var mount = frames[0];
        var div = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        var tailNode = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "tail-0").NodeId;
        var attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
        Task Click() => renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null));

        // ── Toggle ON: three PrependFrames arrive — markup, span, markup ──────
        await Click();
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var on = frames[^1];

        // (b) The span (Blazor sibling 2 — button 0, markup 1) lands at HOST
        // index 1: button(0) · span(1) · tail(2). A markup slot counted as a
        // host view would push it to 2; a DROPPED markup slot would leave the
        // next assertions unresolvable.
        var insertedSpan = Assert.Single(on.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == div.NodeId);
        Assert.Equal(1, insertedSpan.InsertIndex);
        Assert.Single(on.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "inserted");

        // (a→ON) The SAME diff's tail UpdateText arrives at the POST-insert
        // sibling index (markup slots included) — it must hit the tail node.
        var onTail = Assert.Single(on.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "tail-1");
        Assert.Equal(tailNode, onTail.NodeId);

        // ── Toggle OFF: RemoveFrame ×3 — two of them MARKUP slots ─────────────
        await Click();
        var off = frames[^1];

        // Only the span owns a host view: exactly ONE RemoveNode; the two
        // markup-slot removals trim silently.
        var removed = Assert.Single(off.Patches.OfType<RemoveNodePatch>());
        Assert.Equal(insertedSpan.NodeId, removed.NodeId);

        // (a→OFF) The tail edit now arrives at the POST-remove sibling index —
        // stale markup slots would resolve it one (or two) slots late.
        var offTail = Assert.Single(off.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "tail-2");
        Assert.Equal(tailNode, offTail.NodeId);

        // ── Toggle ON again: the trimmed list must translate a fresh insert ───
        await Click();
        var on2 = frames[^1];
        var reinserted = Assert.Single(on2.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == div.NodeId);
        Assert.Equal(1, reinserted.InsertIndex);
        var on2Tail = Assert.Single(on2.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "tail-3");
        Assert.Equal(tailNode, on2Tail.NodeId);
    }

    // ── Non-whitespace markup: unrepresentable natively ───────────────────────

    /// <summary>Raw HTML markup with a change-driven span AFTER it — the span
    /// is what proves the violating markup's slot is KEPT on the tolerated
    /// path (review F4): Blazor numbers the span at sibling 2 (input 0,
    /// markup 1), so its edits only resolve if the dropped markup still
    /// occupies slot 1.</summary>
    private sealed class RawHtmlMarkup : ComponentBase
    {
        private string _text = "start";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.OpenElement(1, "input");
            b.AddAttribute(2, "onchange",
                EventCallback.Factory.Create<ChangeEventArgs>(this, e => _text = e.Value?.ToString() ?? ""));
            b.CloseElement();
            b.AddMarkupContent(3, "<b>native has no innerHTML</b>");
            b.OpenElement(4, "span");
            b.AddContent(5, _text);
            b.CloseElement();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task NonWhitespaceMarkup_Strict_Throws()
    {
        var (renderer, _) = BuildRenderer(strict: true);
        using var __ = renderer;

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => renderer.MountAsync<RawHtmlMarkup>(ParameterView.Empty));
        Assert.Contains("markup", Flatten(ex), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonWhitespaceMarkup_NonStrict_DropsButKeepsTheSlot()
    {
        var (renderer, frames) = BuildRenderer(strict: false);
        using var _ = renderer;

        await renderer.MountAsync<RawHtmlMarkup>(ParameterView.Empty);
        Assert.NotEmpty(frames);
        var mount = frames[0];

        // DROPS: exactly div + input + span + the span's text — no node, no
        // text, nothing half-rendered for the raw markup.
        Assert.Equal(4, mount.Patches.OfType<CreateNodePatch>().Count());
        var textNode = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "start").NodeId;

        // KEEPS THE SLOT (the test's name, made true — review F4): the span's
        // change-driven UpdateText arrives at sibling 2, PAST the violating
        // markup's slot. Report-then-forget-the-slot would resolve it into
        // nothing (poisoned StepIn) and this Assert.Single finds no patch.
        var attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "change");
        await renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "change", "typed"));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");

        var replaced = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>());
        Assert.Equal(textNode, replaced.NodeId);
        Assert.Equal("typed", replaced.Text);
    }

    // ── UpdateMarkup: dynamic markup changing IN PLACE (review F2) ────────────
    //
    // Blazor diffs same-sequence Markup frames by CONTENT and emits an
    // UpdateMarkup edit when it changes (@((MarkupString)x) with mutating x).
    // Until the F2 fix the diff switch had no UpdateMarkup arm and no default:
    // the strict contract was silently bypassable on the update path.

    /// <summary>Whitespace markup whose content turns into raw HTML on click;
    /// the tail span (text changed in the SAME diff) sits AFTER the markup
    /// sibling, so the test also proves the slot stays aligned on the
    /// tolerated path.</summary>
    private sealed class MarkupTurnsRaw : ComponentBase
    {
        private string _markup = "\n    ";
        private string _tail = "tail";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.OpenElement(1, "button");
            b.AddAttribute(2, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () =>
                {
                    _markup = "<b>now raw</b>";
                    _tail = "after";
                }));
            b.CloseElement();
            b.AddMarkupContent(3, _markup);
            b.OpenElement(4, "span");
            b.AddContent(5, _tail);
            b.CloseElement();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task UpdateMarkup_ToNonWhitespace_Strict_Throws()
    {
        var (renderer, frames) = BuildRenderer(strict: true);
        using var _ = renderer;

        await renderer.MountAsync<MarkupTurnsRaw>(ParameterView.Empty);
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null)));
        Assert.Contains("UpdateMarkup", Flatten(ex));
        Assert.Contains("now raw", Flatten(ex));
    }

    [Fact]
    public async Task UpdateMarkup_ToNonWhitespace_NonStrict_LogsAndContinues_SlotStaysAligned()
    {
        var (renderer, frames) = BuildRenderer(strict: false);
        using var _ = renderer;

        await renderer.MountAsync<MarkupTurnsRaw>(ParameterView.Empty);
        var mount = frames[0];
        var tailNode = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "tail").NodeId;
        var attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>());

        // Tolerated: the dispatch completes (no throw) …
        await renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");

        // … the markup still renders as nothing, and the SAME diff's tail
        // UpdateText — a sibling AFTER the markup slot — resolves the right
        // node: the slot survived the tolerated violation.
        var replaced = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>());
        Assert.Equal(tailNode, replaced.NodeId);
        Assert.Equal("after", replaced.Text);
    }

    /// <summary>Whitespace to DIFFERENT whitespace: an UpdateMarkup edit
    /// arrives, but the wire never sees whitespace — the frame must carry
    /// nothing but its commit.</summary>
    private sealed class WhitespaceMarkupReflows : ComponentBase
    {
        private string _markup = "\n";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.OpenElement(1, "button");
            b.AddAttribute(2, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _markup = "\n    "));
            b.CloseElement();
            b.AddMarkupContent(3, _markup);
            b.CloseElement();
        }
    }

    [Fact]
    public async Task UpdateMarkup_WhitespaceToWhitespace_IsAWireNoOp()
    {
        var (renderer, frames) = BuildRenderer(strict: true);
        using var _ = renderer;

        await renderer.MountAsync<WhitespaceMarkupReflows>(ParameterView.Empty);
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // Strict mode — a violation would throw; and the re-render frame is
        // commit-only: whitespace stays wire-invisible on the update path.
        await renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var patch = Assert.Single(frames[^1].Patches);
        Assert.IsType<CommitFramePatch>(patch);
    }

    private static string Flatten(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            sb.Append(e.Message).Append(" | ");
        return sb.ToString();
    }
}
