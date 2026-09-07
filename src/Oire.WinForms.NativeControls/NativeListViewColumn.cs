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
    /// <summary>Width that fits the widest cell in the column.</summary>
    public const int AutoSizeToContent = -1;

    /// <summary>Width that fits the header text.</summary>
    public const int AutoSizeToHeader = -2;

    private string _text;
    private int _width;
    private NativeSortOrder _sortOrder;
    private NativeColumnAlignment _alignment;

    /// <summary>Creates a column.</summary>
    /// <param name="text">The header text.</param>
    /// <param name="width">
    /// The width in pixels, or one of <see cref="AutoSizeToContent"/> and
    /// <see cref="AutoSizeToHeader"/>.
    /// </param>
    /// <param name="alignment">
    /// How the column's text is aligned. The first column of a report-mode list is always
    /// left-aligned by the control itself; this is honored from the second column onward.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public NativeListViewColumn(string text, int width, NativeColumnAlignment alignment = NativeColumnAlignment.Left) {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _width = width;
        _alignment = alignment;
    }

    /// <summary>The header text.</summary>
    /// <exception cref="ArgumentNullException">The assigned value is null.</exception>
    public string Text {
        get => _text;
        set {
            ArgumentNullException.ThrowIfNull(value);
            _text = value;
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

    /// <summary>
    /// How the column's text is aligned. Assigning it updates the control if the column is in
    /// one.
    /// </summary>
    /// <remarks>
    /// Read back from the control while the column is in one, rather than remembered, so the
    /// property cannot drift from it: an update the control refuses reports the alignment still
    /// in force instead of the one that failed to take.
    /// <para>
    /// The first column of a report-mode list is the exception, and a Win32 rule rather than a
    /// choice made here: the control stores and returns whatever format it is given for column
    /// zero, but always draws that column left-aligned. So an alignment assigned there reads
    /// back as assigned while the list still shows it left-aligned.
    /// </para>
    /// </remarks>
    public NativeColumnAlignment Alignment {
        get => ListView?.GetColumnAlignment(Index) ?? _alignment;
        set {
            _alignment = value;
            ListView?.UpdateColumnAlignment(Index, value);
        }
    }

    /// <summary>
    /// The sort arrow drawn in this column's header. Purely an indicator: the control does not
    /// sort, and setting this does not reorder anything.
    /// </summary>
    /// <remarks>
    /// Read back from the header while the column is in a control, as <see cref="Width"/> and
    /// <see cref="Alignment"/> are, so the property reports the arrow actually drawn.
    /// </remarks>
    public NativeSortOrder SortOrder {
        get => ListView?.GetSortIndicator(Index) ?? _sortOrder;
        set {
            _sortOrder = value;
            ListView?.UpdateSortIndicator(Index, value);
        }
    }

    /// <summary>The control this column belongs to, or null while it is detached.</summary>
    public NativeListView? ListView { get; internal set; }

    /// <summary>Position in the control, or -1 while the column is detached.</summary>
    public int Index { get; internal set; } = -1;

    /// <summary>
    /// The last width assigned to this column, which is what a fresh window is built with.
    /// Not necessarily the constructor's, since the setter updates it too.
    /// </summary>
    internal int InitialWidth => _width;

    /// <inheritdoc />
    public override string ToString() => _text;
}
