using System.Collections.ObjectModel;
using BlazorNative.Renderer;

namespace BlazorNative.Testing;

// ─────────────────────────────────────────────────────────────────────────────
// THE SUPPORTED PROJECTION — the reason this package exists at all (#25).
//
// A test harness has to let an author assert what their page rendered. The only
// description of that inside the framework is `RenderPatch[]`, and the whole
// patch hierarchy is tier NOT-API: "the renderer's IN-MEMORY model, not the wire
// format… the framework re-shapes it freely". Publishing an assertion vocabulary
// over those types would freeze the in-memory model by the back door, and it
// would hand the author the wrong noun — nobody wants to assert that a
// CreateNodePatch has InsertIndex -1. They want "the root has three children and
// the second is a text reading Hello".
//
// So the patches are consumed HERE, once, and never surface. What a consumer
// sees is a materialized widget tree that survives the patch model changing
// underneath it. THIS is what freezes at 1.0; the patch records stay free.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One node of the rendered widget tree, as a test sees it.</summary>
/// <remarks>A value snapshot, not a live view: re-read <see cref="BnTestHost.Tree"/>
/// after driving an event rather than holding a node across a re-render.</remarks>
public sealed class BnTestNode
{
    internal BnTestNode(int id, string nodeType)
    {
        Id = id;
        NodeType = nodeType;
    }

    /// <summary>The renderer's internal node id. Exposed only because a failure
    /// message is far more useful with it; <b>do not assert on it</b> — ids are
    /// allocation order, so inserting an unrelated component upstream renumbers
    /// every node after it. Assert on structure, which is stable.</summary>
    public int Id { get; }

    /// <summary>The widget class: <c>view</c>, <c>text</c>, <c>button</c>,
    /// <c>input</c>, <c>image</c>, <c>scroll</c>, <c>picker</c>, <c>checkbox</c>,
    /// <c>switch</c>, <c>slider</c>, <c>modal</c>, <c>activityindicator</c>.</summary>
    public string NodeType { get; }

    /// <summary>This node's children <b>in their final on-screen order</b>.</summary>
    /// <remarks>
    /// <para><b>This is the member the package exists for.</b> Producing it IS the
    /// shells' own insert algorithm, and Blazor's render queue does not create
    /// children in sibling order — a chained child component renders after its
    /// later siblings. A consumer who re-derived this by walking creates in patch
    /// order would get a test that passes while the app renders in a different
    /// order than it asserts.</para>
    /// </remarks>
    public IReadOnlyList<BnTestNode> Children => _children;

    /// <summary>The node's text, for a text-bearing widget; null otherwise.</summary>
    public string? Text { get; internal set; }

    /// <summary>Styles applied to this node, last write wins. A style whose value
    /// is null was <em>reset</em> — the wire's reset shape — and is kept as a null
    /// entry rather than removed, so a test can tell "reset" from "never set".</summary>
    public IReadOnlyDictionary<string, string?> Styles => _styles;

    /// <summary>Non-style properties (<c>value</c>, <c>placeholder</c>, …).</summary>
    public IReadOnlyDictionary<string, string?> Props => _props;

    /// <summary>Event names currently attached to this node (<c>click</c>,
    /// <c>change</c>, <c>scroll</c>, …). A detached event is removed.</summary>
    public IReadOnlyCollection<string> Events => _events;

    internal readonly List<BnTestNode> _children = [];
    internal readonly Dictionary<string, string?> _styles = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, string?> _props = new(StringComparer.Ordinal);
    internal readonly HashSet<string> _events = new(StringComparer.Ordinal);

    /// <summary>Handler ids by event name — internal, because a handler id is the
    /// renderer's bookkeeping and dispatching is <see cref="BnTestHost"/>'s job.</summary>
    internal readonly Dictionary<string, int> _handlers = new(StringComparer.Ordinal);

    /// <summary>A short structural description, for failure messages.</summary>
    public override string ToString()
        => Text is null
            ? $"{NodeType}#{Id}({Children.Count} children)"
            : $"{NodeType}#{Id}(\"{Text}\")";
}

/// <summary>The rendered widget tree after every frame so far.</summary>
/// <remarks>Deliberately <b>not</b> a frame-by-frame history: how many frames a
/// change took is renderer-internal behaviour, and pinning it in consumer tests
/// would make every renderer optimisation a breaking change.</remarks>
public sealed class BnTestTree
{
    private readonly Dictionary<int, BnTestNode> _byId = [];
    private readonly List<BnTestNode> _roots = [];

    /// <summary>Collapsed text child → the node it was folded into.</summary>
    /// <remarks>
    /// THE TEXT-CHILD COLLAPSE, and the projection must do it because the SHELLS do
    /// it. `&lt;BnText Text="Hello" /&gt;` renders a `text` node with a child text node
    /// carrying the content — but neither shell gives that child its own widget: a
    /// text frame whose parent is a text-BEARING leaf (a TextView, a Button, an
    /// EditText — not a container) is aliased onto the parent and its content becomes
    /// the parent's own.
    ///
    /// Without mirroring that here, a consumer writing the most ordinary assertion
    /// there is — `Assert.Equal("Hello", node.Text)` — would get null and have to
    /// learn that their text lives one level down in a node that does not exist on
    /// screen. The harness would be describing the render tree instead of the app.
    /// </remarks>
    private readonly Dictionary<int, int> _collapsed = [];

    /// <summary>Node types that ABSORB a lone text child, mirroring the shells:
    /// a container keeps its children, a text-bearing leaf swallows them.</summary>
    private static readonly HashSet<string> TextBearing =
        new(StringComparer.Ordinal) { "text", "button", "input" };

    /// <summary>The single root node the mounted component produced.</summary>
    /// <exception cref="InvalidOperationException">If the component produced no
    /// root, or more than one — both of which are worth failing loudly on, since a
    /// silently-empty tree makes every later assertion vacuous.</exception>
    public BnTestNode Root => _roots.Count switch
    {
        1 => _roots[0],
        0 => throw new InvalidOperationException(
            "the mounted component produced no root node. It rendered nothing, or it rendered only "
            + "non-widget content — remember that only Bn* components map to widgets; a bare <div> "
            + "is not a node."),
        _ => throw new InvalidOperationException(
            $"the mounted component produced {_roots.Count} root nodes; use Roots when a component "
            + "renders a fragment rather than a single element."),
    };

    /// <summary>Every root, for a component that renders a fragment.</summary>
    public IReadOnlyList<BnTestNode> Roots => _roots;

    /// <summary>Every node of the given widget class, in document order.</summary>
    public IReadOnlyList<BnTestNode> FindAll(string nodeType)
    {
        var found = new List<BnTestNode>();
        foreach (BnTestNode root in _roots) Walk(root, n =>
        {
            if (string.Equals(n.NodeType, nodeType, StringComparison.Ordinal)) found.Add(n);
        });
        return found;
    }

    /// <summary>The single node whose text is <paramref name="text"/> — the
    /// everyday "find the label I rendered" lookup.</summary>
    /// <exception cref="InvalidOperationException">If no node, or more than one,
    /// carries that text. Both are assertion mistakes worth naming.</exception>
    public BnTestNode FindText(string text)
    {
        var found = new List<BnTestNode>();
        foreach (BnTestNode root in _roots) Walk(root, n =>
        {
            if (string.Equals(n.Text, text, StringComparison.Ordinal)) found.Add(n);
        });

        return found.Count switch
        {
            1 => found[0],
            0 => throw new InvalidOperationException(
                $"no node has the text \"{text}\". Rendered texts: "
                + string.Join(", ", AllTexts().Select(t => $"\"{t}\""))),
            _ => throw new InvalidOperationException(
                $"{found.Count} nodes have the text \"{text}\" — narrow the assertion, or use FindAll."),
        };
    }

    private IEnumerable<string> AllTexts()
    {
        var texts = new List<string>();
        foreach (BnTestNode root in _roots) Walk(root, n => { if (n.Text is not null) texts.Add(n.Text); });
        return texts;
    }

    private static void Walk(BnTestNode node, Action<BnTestNode> visit)
    {
        visit(node);
        foreach (BnTestNode child in node.Children) Walk(child, visit);
    }

    // ── Applying frames — the patch model is consumed here and nowhere else ──

    internal void Apply(RenderFrame frame)
    {
        foreach (RenderPatch patch in frame.Patches)
        {
            switch (patch)
            {
                case CreateNodePatch p:
                {
                    // The collapse (see [_collapsed]): a text node under a text-bearing
                    // leaf is that leaf's content, not a node of its own.
                    if (p.NodeType == "text"
                        && p.ParentId is int owner
                        && _byId.TryGetValue(Resolve(owner), out BnTestNode? host)
                        && TextBearing.Contains(host.NodeType))
                    {
                        _collapsed[p.NodeId] = host.Id;
                        break;
                    }

                    var node = new BnTestNode(p.NodeId, p.NodeType);
                    _byId[p.NodeId] = node;

                    List<BnTestNode> siblings = p.ParentId is int parentId && _byId.TryGetValue(Resolve(parentId), out BnTestNode? parent)
                        ? parent._children
                        : _roots;

                    // THE INSERT ALGORITHM, and it is the shells' own: -1 appends
                    // (the mount-walk common case), >= 0 inserts at that host child
                    // index (mid-list keyed inserts — 0 is a VALID front index,
                    // which is exactly why -1 is encoded explicitly rather than
                    // using 0 as a sentinel). Getting this wrong is the failure
                    // this package exists to stop a consumer re-deriving.
                    if (p.InsertIndex < 0 || p.InsertIndex >= siblings.Count)
                        siblings.Add(node);
                    else
                        siblings.Insert(p.InsertIndex, node);
                    break;
                }

                case RemoveNodePatch p:
                    Remove(p.NodeId);
                    break;

                case ReplaceTextPatch p when _byId.TryGetValue(Resolve(p.NodeId), out BnTestNode? n):
                    n.Text = p.Text;
                    break;

                case SetStylePatch p when _byId.TryGetValue(Resolve(p.NodeId), out BnTestNode? n):
                    // A null value is the wire's RESET. Kept as a null entry rather
                    // than removed so a test can distinguish "reset to default" from
                    // "never set" — the same distinction the shells act on.
                    n._styles[p.Property] = p.Value;
                    break;

                case UpdatePropPatch p when _byId.TryGetValue(Resolve(p.NodeId), out BnTestNode? n):
                    n._props[p.Name] = p.Value;
                    break;

                case AttachEventPatch p when _byId.TryGetValue(Resolve(p.NodeId), out BnTestNode? n):
                    // Re-attach REPLACES (last wins) — the wire contract.
                    n._events.Add(p.EventName);
                    n._handlers[p.EventName] = p.HandlerId;
                    break;

                case DetachEventPatch p when _byId.TryGetValue(Resolve(p.NodeId), out BnTestNode? n):
                    n._events.Remove(p.EventName);
                    n._handlers.Remove(p.EventName);
                    break;

                // CommitFrame is a boundary marker with no tree state, and a patch
                // for a node this tree never saw is ignored rather than throwing:
                // the harness must not turn a renderer quirk into a test crash.
            }
        }
    }

    /// <summary>Follows the collapse alias — a patch addressed to a folded text
    /// child belongs to the node that swallowed it.</summary>
    private int Resolve(int nodeId) => _collapsed.TryGetValue(nodeId, out int owner) ? owner : nodeId;

    internal bool TryGet(int nodeId, out BnTestNode node) => _byId.TryGetValue(Resolve(nodeId), out node!);

    private void Remove(int nodeId)
    {
        _collapsed.Remove(nodeId);
        if (!_byId.TryGetValue(nodeId, out BnTestNode? node)) return;

        // Depth-first so the id map does not keep descendants alive — a removed
        // subtree that stayed addressable would let a stale assertion pass.
        foreach (BnTestNode child in node.Children.ToArray()) Remove(child.Id);

        _byId.Remove(nodeId);
        _roots.Remove(node);
        foreach (BnTestNode candidate in _byId.Values) candidate._children.Remove(node);
    }
}
