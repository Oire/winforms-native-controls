namespace Oire.WinForms.NativeControls;

/// <summary>How a column's text is aligned.</summary>
public enum NativeColumnAlignment {
    /// <summary>Aligned to the leading edge.</summary>
    Left,

    /// <summary>Aligned to the trailing edge.</summary>
    Right,

    /// <summary>Centered.</summary>
    Center,
}

/// <summary>Which way a column is sorted, as shown by the arrow in its header.</summary>
public enum NativeSortOrder {
    /// <summary>No arrow.</summary>
    None,

    /// <summary>Ascending: the arrow points up.</summary>
    Ascending,

    /// <summary>Descending: the arrow points down.</summary>
    Descending,
}

/// <summary>One column of a <see cref="NativeListView"/>.</summary>
public sealed class NativeListViewColumn {
    private string _text;
    private int _width;
    private NativeSortOrder _sortOrder;

    /// <summary>Creates a column.</summary>
    /// <param name="text">The header text.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="alignment">
    /// How the column's text is aligned. The first column of a report-mode list is always
    /// left-aligned by the control itself; this is honored from the second column onward.
    /// </param>
    public NativeListViewColumn(string text, int width, NativeColumnAlignment alignment = NativeColumnAlignment.Left) {
        _text = text ?? String.Empty;
        _width = width;
        Alignment = alignment;
    }

    /// <summary>The header text.</summary>
    public string Text {
        get => _text;
        set {
            _text = value ?? String.Empty;
            ListView?.UpdateColumn(this);
        }
    }

    /// <summary>The column width in pixels.</summary>
    public int Width {
        get => ListView?.GetColumnWidth(Index) ?? _width;
        set {
            _width = value;
            ListView?.SetColumnWidth(Index, value);
        }
    }

    /// <summary>How the column's text is aligned.</summary>
    public NativeColumnAlignment Alignment { get; }

    /// <summary>
    /// The sort arrow drawn in this column's header. Purely an indicator: the control does not
    /// sort, and setting this does not reorder anything.
    /// </summary>
    public NativeSortOrder SortOrder {
        get => _sortOrder;
        set {
            _sortOrder = value;
            ListView?.UpdateSortIndicator(Index, value);
        }
    }

    /// <summary>The control this column belongs to, or null while it is detached.</summary>
    public NativeListView? ListView { get; internal set; }

    /// <summary>Position in the control, or -1 while the column is detached.</summary>
    public int Index { get; internal set; } = -1;

    /// <summary>The width this column was created with, before the control resized it.</summary>
    internal int InitialWidth => _width;

    /// <inheritdoc />
    public override string ToString() => _text;
}
