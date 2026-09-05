using System.Collections.ObjectModel;

namespace Oire.WinForms.NativeControls;

/// <summary>
/// One row in a <see cref="NativeListView"/>: the text of each column, plus whatever the
/// application wants to hang off it.
/// </summary>
public sealed class NativeListViewItem {
    private readonly CellCollection _cells;

    /// <summary>Creates a row from its column texts, first column first.</summary>
    public NativeListViewItem(params string[] cells) {
        ArgumentNullException.ThrowIfNull(cells);
        // A List, not the array itself: Collection<T> over an array is read-only, so a
        // cell could never be reassigned afterwards.
        _cells = new CellCollection(this, cells.Length == 0 ? [String.Empty] : new List<string>(cells));
    }

    /// <summary>Creates a row from its column texts, first column first.</summary>
    public NativeListViewItem(IEnumerable<string> cells) : this([.. cells ?? throw new ArgumentNullException(nameof(cells))]) {
    }

    /// <summary>
    /// The column texts of this row. Assigning one updates the control if the row is in one;
    /// a row may carry more cells than the control has columns, and the extras are ignored.
    /// </summary>
    public IList<string> Cells => _cells;

    /// <summary>The first column's text — the row's label as a screen reader announces it.</summary>
    public string Text {
        get => _cells[0];
        set => _cells[0] = value;
    }

    /// <summary>
    /// The row's text color, or null to use the control's. Drawn through custom draw, which
    /// a bare list control needs in order to color one row differently from the rest.
    /// </summary>
    public Color? ForeColor {
        get;
        set {
            field = value;
            ListView?.InvalidateRow(Index);
        }
    }

    /// <summary>Application data. The control neither reads nor interprets it.</summary>
    public object? Tag { get; set; }

    /// <summary>The control this row belongs to, or null while it is detached.</summary>
    public NativeListView? ListView { get; internal set; }

    /// <summary>Position in the control, or -1 while the row is detached.</summary>
    public int Index { get; internal set; } = -1;

    /// <summary>Whether the row is selected. Setting it moves the selection in the control.</summary>
    /// <remarks>
    /// Settable before the control has a window. A list that is populated and selected during
    /// form construction — the usual order — would otherwise come up with nothing selected,
    /// and a list-like control with no selection announces nothing when focus reaches it.
    /// </remarks>
    public bool Selected {
        get => ListView is { HasWindow: true } list ? list.IsSelected(Index) : PendingSelected;
        set {
            PendingSelected = value;
            if (ListView is { HasWindow: true } list) {
                list.SetSelected(Index, value);
            }
        }
    }

    /// <summary>
    /// Whether the row has the focus rectangle — which is what a screen reader follows, and
    /// is not the same thing as being selected.
    /// </summary>
    public bool Focused {
        get => ListView is { HasWindow: true } list ? list.IsFocused(Index) : PendingFocused;
        set {
            PendingFocused = value;
            if (ListView is { HasWindow: true } list) {
                list.SetFocused(Index, value);
            }
        }
    }

    /// <summary>Selection held for a control that has no window yet, or has lost one.</summary>
    internal bool PendingSelected { get; set; }

    /// <summary>Focus held for a control that has no window yet, or has lost one.</summary>
    internal bool PendingFocused { get; set; }

    /// <summary>Scrolls the row into view, if it is in a control.</summary>
    public void EnsureVisible() => ListView?.EnsureVisible(Index);

    /// <inheritdoc />
    public override string ToString() => Text;

    /// <summary>
    /// The row's cells, which push straight through to the control when the row is attached.
    /// </summary>
    private sealed class CellCollection(NativeListViewItem owner, IList<string> cells): Collection<string>(cells) {
        protected override void SetItem(int index, string item) {
            base.SetItem(index, item ?? String.Empty);
            owner.ListView?.UpdateCell(owner.Index, index, item ?? String.Empty);
        }

        protected override void InsertItem(int index, string item) {
            base.InsertItem(index, item ?? String.Empty);
            owner.ListView?.RefreshItem(owner);
        }

        protected override void RemoveItem(int index) {
            base.RemoveItem(index);
            owner.ListView?.RefreshItem(owner);
        }
    }
}
