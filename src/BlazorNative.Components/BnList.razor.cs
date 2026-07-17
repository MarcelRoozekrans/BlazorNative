namespace BlazorNative.Components;

// See BnCheckbox.razor.cs for why the six .razor components carry their
// type-level summary in a partial class. This one is generic, so the partial
// declaration repeats the type parameter — @typeparam TItem in the .razor
// produces `public partial class BnList<TItem>`.

/// <summary>
/// A virtualized list: it renders only the rows near the viewport, so a list of
/// ten thousand items costs about as much as a list of twenty.
/// </summary>
/// <typeparam name="TItem">The item type. Rows are keyed by the item itself.</typeparam>
/// <remarks>
/// <para>
/// It is a <see cref="BnScroll"/> with the off-screen rows replaced by two
/// spacers, so the scrollbar is the size it would be if every row existed.
/// <c>ItemHeight</c> and <c>Height</c> are required and must be positive: the
/// window arithmetic is exact, which is what makes the frames predictable, and it
/// needs both numbers up front. Rows are a fixed height — a variable-height list
/// is a different component and this is not it.
/// </para>
/// <para>
/// <b>Items must be distinct.</b> Rows are keyed by the item, which is what lets
/// a row's state — an open editor, the text in a box, the focus — travel with the
/// item rather than with its position as the list scrolls. Two equal items in one
/// window will throw.
/// </para>
/// <para>
/// <b>A row that scrolls out is destroyed</b>, and its row-local state goes with
/// it. Keep state that must survive in your own model, not in the row.
/// <c>Overscan</c> (default 4) renders a few extra rows beyond each edge to
/// absorb the latency of a fast fling.
/// </para>
/// <example>
/// <code>
/// &lt;BnList Items="_rows" ItemHeight="44" Height="400"&gt;
///     &lt;RowTemplate&gt;
///         &lt;BnText Text="@context.Name" /&gt;
///     &lt;/RowTemplate&gt;
/// &lt;/BnList&gt;
/// </code>
/// </example>
/// </remarks>
public partial class BnList<TItem>
{
}
