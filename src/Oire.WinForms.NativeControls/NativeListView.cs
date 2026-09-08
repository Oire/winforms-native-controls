using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Oire.WinForms.NativeControls;

/// <summary>
/// A report-mode list backed by a genuine <c>SysListView32</c> window, so screen readers read
/// every column instead of only the first.
/// </summary>
/// <remarks>
/// <para>
/// A WinForms <c>ListView</c> in Details view is a <c>SysListView32</c> underneath, but WinForms
/// registers its own window class to own the <c>WndProc</c>, so the window is called
/// <c>WindowsForms10.SysListView32.app.0…</c>. UI Automation chooses its built-in
/// common-control provider <em>by window class name</em>, and so does NVDA's own list handling.
/// Neither matches that name, so the control is described either as a <c>Table</c> whose
/// <c>GridPattern.GetItem</c> returns unusable elements, or — if WinForms' provider is declined
/// — as a bare <c>Pane</c> with no items at all. Measured by ear across JAWS, NVDA and
/// Narrator: every WinForms variant reads only the first column.
/// </para>
/// <para>
/// No subclass of <c>ListView</c> can change its window class, so this control does not derive
/// from one. It creates a real <c>SysListView32</c> child window and drives it with
/// <c>LVM_*</c> messages — the same window class wxWidgets creates, and the same result: every
/// column read, on every reader.
/// </para>
/// <para>
/// The WinForms control is the container and the list is its child. Focus arriving here is
/// forwarded to the child; Tab and Shift+Tab inside the child move through the surrounding
/// WinForms controls, since the child is not one of them.
/// </para>
/// </remarks>
/// <remarks>
/// Left unsealed deliberately, unlike every other public type here. A WinForms control is
/// conventionally extensible, and the protected surface below - <see cref="DefaultSize"/>,
/// <see cref="IsInputKey"/>, the handle and theme hooks - is the documented way to specialize
/// one. A derived type must call the base implementation of any of these it overrides; the
/// list window is created and torn down in them.
/// </remarks>
[DesignerCategory("Code")]
[DefaultEvent(nameof(SelectedIndexChanged))]
[DefaultProperty(nameof(Items))]
public class NativeListView: Control {
    private readonly List<NativeListViewItem> _items = [];
    private readonly List<NativeListViewColumn> _columns = [];
    private readonly ItemCollection _itemCollection;
    private readonly ColumnCollection _columnCollection;

    private IntPtr _listHandle;
    private IntPtr _fontHandle;
    private ChildMessageFilter? _childSubclass;
    private ChildDropTarget? _dropTarget;
    private bool _multiSelect;
    private BorderStyle _borderStyle = BorderStyle.Fixed3D;

    // Null means "not set", which is what separates the window default below from a caller
    // who deliberately asked for that same color. Color.Empty stores as null: WinForms treats
    // assigning it as a reset, and Control.ResetBackColor is exactly that assignment.
    private Color? _backColor;
    private Color? _foreColor;
    private int _insertionIndex = -1;
    private bool _insertionAfter;
    private Size? _defaultSize;

    /// <summary>Rows a list asks room for before anything has told it how big to be.</summary>
    private const int DefaultVisibleRows = 5;

    /// <summary>Characters of width a list asks for, on the same basis.</summary>
    private const int DefaultVisibleCharacters = 20;

    /// <summary>Creates an empty list.</summary>
    public NativeListView() {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, false);
        TabStop = true;
        _itemCollection = new ItemCollection(this, _items);
        _columnCollection = new ColumnCollection(this, _columns);
    }

    /// <summary>The rows, in display order.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IList<NativeListViewItem> Items => _itemCollection;

    /// <summary>The columns, in display order.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IList<NativeListViewColumn> Columns => _columnCollection;

    /// <summary>
    /// Whether more than one row can be selected at a time. Changing it after the handle
    /// exists recreates the list window, because it is part of the creation style.
    /// </summary>
    [DefaultValue(false)]
    [Category("Behavior")]
    [Description("Whether more than one row can be selected at a time.")]
    public bool MultiSelect {
        get => _multiSelect;
        set {
            if (_multiSelect == value) {
                return;
            }

            _multiSelect = value;
            if (IsHandleCreated) {
                RecreateListWindow();
            }
        }
    }

    /// <summary>The selected rows, in display order. Empty when nothing is selected.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<NativeListViewItem> SelectedItems {
        get {
            if (_listHandle == IntPtr.Zero) {
                return [];
            }

            var selected = new List<NativeListViewItem>();
            var index = -1;
            while (true) {
                index = (int)ListViewInterop.SendMessageW(
                    _listHandle, ListViewInterop.LVM_GETNEXTITEM, index,
                    (IntPtr)ListViewInterop.LVNI_SELECTED);

                if (index < 0 || index >= _items.Count) {
                    break;
                }

                selected.Add(_items[index]);
            }

            return selected;
        }
    }

    /// <summary>The row carrying the focus rectangle, or null when there is none.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public NativeListViewItem? FocusedItem {
        get {
            if (_listHandle == IntPtr.Zero) {
                return null;
            }

            var index = (int)ListViewInterop.SendMessageW(
                _listHandle, ListViewInterop.LVM_GETNEXTITEM, -1, (IntPtr)ListViewInterop.LVNI_FOCUSED);

            return index >= 0 && index < _items.Count ? _items[index] : null;
        }
    }

    /// <summary>
    /// The border drawn around the list. Defaults to <see cref="BorderStyle.Fixed3D"/>, which
    /// is what a WinForms <c>ListView</c> and <c>TreeView</c> both use - a list without one
    /// sits flush against its panel while its neighbors are inset, and reads as misaligned.
    /// </summary>
    [DefaultValue(BorderStyle.Fixed3D)]
    [Category("Appearance")]
    [Description("The border drawn around the list. Fixed3D matches a WinForms ListView.")]
    public BorderStyle BorderStyle {
        get => _borderStyle;
        set {
            if (_borderStyle == value) {
                return;
            }

            _borderStyle = value;

            // Part of the creation style for a common control.
            if (IsHandleCreated) {
                RecreateListWindow();
            }
        }
    }

    /// <summary>
    /// Whether the control accepts drops. Registered on the list window as well as the
    /// container, because the list covers it and OLE resolves a drop against the window
    /// under the cursor.
    /// </summary>
    public override bool AllowDrop {
        get => base.AllowDrop;
        set {
            base.AllowDrop = value;
            UpdateDropTarget();
        }
    }

    /// <summary>
    /// The name a screen reader announces for the list.
    /// </summary>
    /// <remarks>
    /// Shadows <see cref="Control.AccessibleName"/> because the name has to reach the list
    /// window, not the container a reader never sees, and the base property is not virtual.
    /// Assigning through a <see cref="Control"/>-typed reference therefore sets the name
    /// without forwarding it; call <see cref="RefreshAccessibleName"/> if that happens.
    /// <para>
    /// Deliberately left serializable, unlike the other shadowed and collection properties on
    /// this control: a designer that showed this in the property grid and then dropped it on
    /// save would silently discard the one property the control exists for.
    /// </para>
    /// </remarks>
    // Visible, not Hidden: a designer that showed this in the grid and then dropped it on save
    // would silently discard the one property this control exists for.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    [Category("Accessibility")]
    [Description("The name a screen reader announces for the list. The most important property here.")]
    public new string? AccessibleName {
        get => base.AccessibleName;
        set {
            base.AccessibleName = value;
            ApplyAccessibleName();
        }
    }

    /// <summary>
    /// Pushes <see cref="AccessibleName"/> to the list window again — after a language
    /// change, or any assignment that did not go through this type.
    /// </summary>
    public void RefreshAccessibleName() => ApplyAccessibleName();

    /// <summary>Whether the list window exists yet. State lives in the model until it does.</summary>
    internal bool HasWindow => _listHandle != IntPtr.Zero;

    /// <summary>The <c>SysListView32</c> window itself, for callers that need to talk to it.</summary>
    /// <remarks>
    /// <see cref="IntPtr.Zero"/> until the control has a handle, and again once it is disposed
    /// or its handle is recreated. Read it afresh each time rather than holding on to it.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IntPtr ListHandle => _listHandle;

    /// <summary>Raised when the set of selected rows changes.</summary>
    [Category("Behavior")]
    [Description("Raised when the set of selected rows changes.")]
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised when a column header is clicked.</summary>
    [Category("Action")]
    [Description("Raised when a column header is clicked.")]
    public event EventHandler<NativeColumnClickEventArgs>? ColumnClick;

    /// <summary>Raised on double-click or Enter — the row the user means to open.</summary>
    [Category("Action")]
    [Description("Raised on double-click or Enter: the row the user means to open.")]
    public event EventHandler<NativeListViewItemEventArgs>? ItemActivate;

    /// <summary>Raised when the user starts dragging a row.</summary>
    [Category("Action")]
    [Description("Raised when the user starts dragging a row.")]
    public event EventHandler<NativeListViewItemEventArgs>? ItemDrag;

    /// <summary>Scrolls the row at <paramref name="index"/> into view.</summary>
    /// <param name="index">The row to reveal. Out-of-range values are ignored.</param>
    public void EnsureVisible(int index) {
        if (_listHandle != IntPtr.Zero && index >= 0) {
            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_ENSUREVISIBLE, index, IntPtr.Zero);
        }
    }

    /// <summary>Suspends redrawing until <see cref="EndUpdate"/>, for bulk changes.</summary>
    public void BeginUpdate() {
        if (_listHandle != IntPtr.Zero) {
            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Resumes redrawing after <see cref="BeginUpdate"/>.</summary>
    public void EndUpdate() {
        if (_listHandle == IntPtr.Zero) {
            return;
        }

        ListViewInterop.SendMessageW(_listHandle, ListViewInterop.WM_SETREDRAW, 1, IntPtr.Zero);
        Invalidate(true);
    }

    /// <summary>
    /// Draws a drop indicator across the edge of a row: the line that tells a dragging user
    /// where the thing they are holding is about to land.
    /// </summary>
    /// <param name="index">
    /// The row to mark against, or -1 for no mark, which is what <see cref="ClearInsertionMark"/>
    /// passes.
    /// </param>
    /// <param name="after">True to draw below the row, false to draw above it.</param>
    /// <remarks>
    /// <para>
    /// Drawn here rather than left to the application, because an application has no way to do
    /// it. The list is a native child window that paints itself and covers this control, so a
    /// consumer never receives a paint event over it; only the code that already answers the
    /// control's draw notifications can put anything on top.
    /// </para>
    /// <para>
    /// Not the control's own <c>LVM_SETINSERTMARK</c>, which is refused outright in report view
    /// - it returns FALSE and stores nothing. This is an overlay drawn at the post-paint stage,
    /// in <see cref="SystemColors.Highlight"/> so it follows the theme, including high contrast,
    /// and scaled by DPI so it stays visible on a dense display.
    /// </para>
    /// </remarks>
    public void SetInsertionMark(int index, bool after) {
        var previous = _insertionIndex;
        if (previous == index && _insertionAfter == after) {
            return;
        }

        _insertionIndex = index;
        _insertionAfter = after;

        // Both the old line and the new one need repainting, and they are rarely the same row.
        InvalidateRow(previous);
        InvalidateRow(index);
    }

    /// <summary>Removes the drop indicator.</summary>
    public void ClearInsertionMark() => SetInsertionMark(-1, after: false);

    /// <summary>Deselects every row.</summary>
    public void ClearSelection() {
        if (_listHandle == IntPtr.Zero) {
            return;
        }

        foreach (var item in _items) {
            item.PendingSelected = false;
        }

        // An index of -1 applies the state to every row at once.
        var state = new ListViewInterop.LVITEMW { State = 0, StateMask = ListViewInterop.LVIS_SELECTED };
        ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETITEMSTATE, -1, ref state);
    }

    /// <inheritdoc cref="GetItemAt(Point)" />
    /// <param name="x">Client-coordinate x.</param>
    /// <param name="y">Client-coordinate y.</param>
    public NativeListViewItem? GetItemAt(int x, int y) => GetItemAt(new Point(x, y));

    /// <summary>The row at a point in this control's client coordinates.</summary>
    /// <param name="clientPoint">The point to test.</param>
    /// <returns>The row under the point, or null when it is not on one.</returns>
    public NativeListViewItem? GetItemAt(Point clientPoint) {
        if (_listHandle == IntPtr.Zero) {
            return null;
        }

        var hit = new ListViewInterop.LVHITTESTINFO {
            Point = new Win32Interop.POINT { X = clientPoint.X, Y = clientPoint.Y },
        };

        var index = (int)ListViewInterop.SendMessageW(
            _listHandle, ListViewInterop.LVM_HITTEST, IntPtr.Zero, ref hit);

        return index >= 0 && index < _items.Count ? _items[index] : null;
    }

    /// <summary>
    /// The bounds of a row in this control's client coordinates, or an empty rectangle when
    /// there is no such row.
    /// </summary>
    /// <param name="index">The row to measure.</param>
    /// <returns>The row's bounds, or <see cref="Rectangle.Empty"/> when there is no such row.</returns>
    public Rectangle GetItemBounds(int index) {
        if (_listHandle == IntPtr.Zero || index < 0) {
            return Rectangle.Empty;
        }

        // The message reads the wanted portion out of the rectangle it is about to fill.
        var rect = new ListViewInterop.RECT { Left = ListViewInterop.LVIR_BOUNDS };
        var ok = ListViewInterop.SendMessageW(
            _listHandle, ListViewInterop.LVM_GETITEMRECT, index, ref rect);

        return ok == IntPtr.Zero
            ? Rectangle.Empty
            : Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    /// <summary>
    /// The list's background. Both colors are pushed into the list window, which paints itself
    /// and ignores the managed control's colors otherwise.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="SystemColors.Window"/> rather than inheriting the parent's, which
    /// is what a WinForms <c>ListView</c> does too: a list is a data surface, not a panel, and
    /// a list wearing the dialog's gray reads as disabled.
    /// </remarks>
    [Category("Appearance")]
    [Description("The list background. Follows the system theme unless assigned.")]
    public override Color BackColor {
        get => _backColor ?? SystemColors.Window;
        set {
            _backColor = value.IsEmpty ? null : value;
            base.BackColor = value;
            ApplyColors();
        }
    }

    /// <summary>The list's text color. Defaults to <see cref="SystemColors.WindowText"/>.</summary>
    /// <remarks>
    /// A per-row <see cref="NativeListViewItem.ForeColor"/> overrides this for that row.
    /// </remarks>
    [Category("Appearance")]
    [Description("The list text color. Follows the system theme unless assigned.")]
    public override Color ForeColor {
        get => _foreColor ?? SystemColors.WindowText;
        set {
            _foreColor = value.IsEmpty ? null : value;
            base.ForeColor = value;
            ApplyColors();
        }
    }

    /// <summary>
    /// The size the control asks for when nothing else decides, measured from the current
    /// font rather than fixed in pixels — so it follows DPI and the font the user chose,
    /// which a constant cannot.
    /// </summary>
    /// <remarks>
    /// This is not cosmetic. A container that measures its children — a
    /// <c>TableLayoutPanel</c> with an auto-sized row, say — divides a row-spanning
    /// neighbor's height between the rows according to what each row's own children ask for.
    /// A control that asks for nothing gives its whole share away, and the auto-sized row
    /// grows by exactly that much and pushes the list down the panel.
    /// </remarks>
    protected override Size DefaultSize {
        get {
            // Layout reads this repeatedly, and measuring text is not free. It only changes
            // with the font, which clears the cache on its way through OnFontChanged.
            if (_defaultSize is { } cached) {
                return cached;
            }

            var row = Math.Max(FontHeight, 1);
            var sample = new string('0', DefaultVisibleCharacters);

            // A header and a handful of rows tall; a sample line wide.
            var size = new Size(
                TextRenderer.MeasureText(sample, Font).Width,
                row * (DefaultVisibleRows + 1));

            _defaultSize = size;
            return size;
        }
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize) {
        var preferred = base.GetPreferredSize(proposedSize);
        return preferred.IsEmpty ? DefaultSize : preferred;
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        CreateListWindow();
    }

    /// <inheritdoc />
    protected override void OnHandleDestroyed(EventArgs e) {
        DestroyListWindow();
        base.OnHandleDestroyed(e);
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(EventArgs e) {
        base.OnSizeChanged(e);
        if (_listHandle != IntPtr.Zero) {
            ListViewInterop.MoveWindow(_listHandle, 0, 0, ClientSize.Width, ClientSize.Height, repaint: true);
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus(EventArgs e) {
        base.OnGotFocus(e);

        // The container is the tab stop; the list is what the user actually works in, and what
        // a screen reader must land on. Only move focus if it is not already there — setting it
        // again still raises the events a reader reacts to.
        if (_listHandle != IntPtr.Zero && ListViewInterop.GetFocus() != _listHandle) {
            ListViewInterop.SetFocus(_listHandle);
        }
    }

    /// <summary>
    /// Claims the keys the list navigates with, so WinForms dispatches them instead of
    /// treating them as dialog navigation.
    /// </summary>
    /// <remarks>
    /// <see cref="Control.FromChildHandle"/> walks up the parent chain, so a keystroke aimed
    /// at the list window is pre-processed as though it were aimed at this container. Without
    /// this the dialog manager reads an arrow key as "move to the next control" and focus
    /// leaves the list the moment the user tries to move within it. Escape is deliberately not
    /// claimed: a dialog hosting the list still has to be able to cancel.
    /// </remarks>
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown => true,
        // Tab is claimed so the dialog manager leaves it alone; the list window moves focus
        // itself, which is the only way it can land on the right neighbor.
        Keys.Tab => true,
        // Enter activates the focused row rather than the form's default button.
        Keys.Enter => true,
        _ => base.IsInputKey(keyData),
    };

    /// <inheritdoc />
    protected override void OnFontChanged(EventArgs e) {
        base.OnFontChanged(e);
        _defaultSize = null;
        ApplyFont();
    }

    /// <summary>
    /// Follows a theme change - light to dark, or into and out of high contrast - which
    /// arrives long after the window was created and its colors first pushed.
    /// </summary>
    /// <remarks>
    /// Both halves have to be redone. The colors are read afresh from
    /// <see cref="SystemColors"/>, which WinForms remaps for the application's color mode, and
    /// the visual style has to be swapped for the variant matching the new mode.
    /// </remarks>
    protected override void OnSystemColorsChanged(EventArgs e) {
        base.OnSystemColorsChanged(e);
        ApplyWindowTheme();
        ApplyColors();
    }

    /// <inheritdoc />
    protected override void OnRightToLeftChanged(EventArgs e) {
        base.OnRightToLeftChanged(e);

        // Mirroring is part of the creation style for a common control.
        if (IsHandleCreated) {
            RecreateListWindow();
        }
    }

    /// <inheritdoc />
    protected override void WndProc(ref Message m) {
        if (m.Msg == (int)ListViewInterop.WM_NOTIFY && HandleNotification(m.LParam, out var result)) {
            m.Result = result;
            return;
        }

        base.WndProc(ref m);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) {
        if (disposing) {
            DestroyListWindow();
        }

        base.Dispose(disposing);
    }

    // --- Item and column plumbing, called from the model types ---------------------------

    internal bool IsSelected(int index) => HasState(index, ListViewInterop.LVIS_SELECTED);

    internal bool IsFocused(int index) => HasState(index, ListViewInterop.LVIS_FOCUSED);

    internal void SetSelected(int index, bool value) =>
        SetState(index, ListViewInterop.LVIS_SELECTED, value);

    internal void SetFocused(int index, bool value) =>
        SetState(index, ListViewInterop.LVIS_FOCUSED, value);

    internal void UpdateCell(int itemIndex, int cellIndex, string text) {
        if (_listHandle == IntPtr.Zero || itemIndex < 0) {
            return;
        }

        var buffer = Marshal.StringToCoTaskMemUni(text);
        try {
            var item = new ListViewInterop.LVITEMW {
                Mask = ListViewInterop.LVIF_TEXT,
                Item = itemIndex,
                SubItem = cellIndex,
                Text = buffer,
            };

            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETITEMTEXTW, itemIndex, ref item);
        } finally {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    internal void RefreshItem(NativeListViewItem item) {
        if (_listHandle == IntPtr.Zero || item.Index < 0) {
            return;
        }

        for (var cell = 0; cell < item.Cells.Count; cell++) {
            UpdateCell(item.Index, cell, item.Cells[cell]);
        }
    }

    internal void UpdateColumn(NativeListViewColumn column) {
        if (_listHandle == IntPtr.Zero || column.Index < 0) {
            return;
        }

        var buffer = Marshal.StringToCoTaskMemUni(column.Text);
        try {
            var native = new ListViewInterop.LVCOLUMNW {
                Mask = ListViewInterop.LVCF_TEXT,
                Text = buffer,
            };

            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETCOLUMNW, column.Index, ref native);
        } finally {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    /// <summary>
    /// Changes one column's text alignment in place, leaving the rest of its format alone.
    /// </summary>
    /// <remarks>
    /// Read, modify, write rather than a plain write: the sort arrow lives in the same format
    /// word as the alignment bits, so assigning the alignment wholesale would silently clear
    /// an arrow that <see cref="UpdateSortIndicator"/> had put there.
    /// </remarks>
    internal void UpdateColumnAlignment(int index, NativeColumnAlignment alignment) {
        if (_listHandle == IntPtr.Zero || index < 0) {
            return;
        }

        var native = new ListViewInterop.LVCOLUMNW { Mask = ListViewInterop.LVCF_FMT };
        if (ListViewInterop.SendMessageW(
                _listHandle, ListViewInterop.LVM_GETCOLUMNW, index, ref native) == IntPtr.Zero) {
            return;
        }

        native.Mask = ListViewInterop.LVCF_FMT;
        native.Fmt = (native.Fmt & ~ListViewInterop.LVCFMT_JUSTIFYMASK) | ToColumnFormat(alignment);
        ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETCOLUMNW, index, ref native);
    }

    /// <summary>
    /// The alignment a column is actually drawn with, or null when there is no window to ask.
    /// </summary>
    /// <remarks>
    /// Read back rather than remembered, as <see cref="GetColumnWidth"/> is, so the property
    /// cannot drift from the control: a rejected update reports the alignment still in force
    /// instead of the one that failed to take. It also tells the truth about column zero,
    /// which a report-mode list always draws left-aligned whatever it is told.
    /// </remarks>
    internal NativeColumnAlignment? GetColumnAlignment(int index) {
        if (_listHandle == IntPtr.Zero || index < 0) {
            return null;
        }

        var native = new ListViewInterop.LVCOLUMNW { Mask = ListViewInterop.LVCF_FMT };
        if (ListViewInterop.SendMessageW(
                _listHandle, ListViewInterop.LVM_GETCOLUMNW, index, ref native) == IntPtr.Zero) {
            return null;
        }

        return (native.Fmt & ListViewInterop.LVCFMT_JUSTIFYMASK) switch {
            ListViewInterop.LVCFMT_RIGHT => NativeColumnAlignment.Right,
            ListViewInterop.LVCFMT_CENTER => NativeColumnAlignment.Center,
            _ => NativeColumnAlignment.Left,
        };
    }

    /// <summary>The Win32 format bits for a column alignment.</summary>
    private static int ToColumnFormat(NativeColumnAlignment alignment) => alignment switch {
        NativeColumnAlignment.Right => ListViewInterop.LVCFMT_RIGHT,
        NativeColumnAlignment.Center => ListViewInterop.LVCFMT_CENTER,
        _ => ListViewInterop.LVCFMT_LEFT,
    };

    /// <summary>
    /// The width the control is actually using, or null when there is no window to ask.
    /// </summary>
    /// <remarks>
    /// Nullable rather than zero. A column added before the handle exists - which is the usual
    /// order, since controls are populated in a constructor and realized later - would otherwise
    /// report a width of zero instead of the width it was given, and an auto-sizing column would
    /// report zero rather than the negative sentinel the caller passed in.
    /// </remarks>
    internal int? GetColumnWidth(int index) =>
        _listHandle == IntPtr.Zero || index < 0
            ? null
            : (int)ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_GETCOLUMNWIDTH, index, IntPtr.Zero);

    internal void SetColumnWidth(int index, int width) {
        if (_listHandle != IntPtr.Zero && index >= 0) {
            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETCOLUMNWIDTH, index, width);
        }
    }

    /// <summary>
    /// The sort arrow the header is actually drawing, or null when there is no header to ask.
    /// </summary>
    internal NativeSortOrder? GetSortIndicator(int index) {
        if (_listHandle == IntPtr.Zero || index < 0) {
            return null;
        }

        var header = ListViewInterop.SendMessageW(
            _listHandle, ListViewInterop.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        if (header == IntPtr.Zero) {
            return null;
        }

        var item = new ListViewInterop.HDITEMW { Mask = ListViewInterop.HDI_FORMAT };
        if (ListViewInterop.SendMessageW(
                header, ListViewInterop.HDM_GETITEMW, index, ref item) == IntPtr.Zero) {
            return null;
        }

        if ((item.Fmt & ListViewInterop.HDF_SORTUP) != 0) {
            return NativeSortOrder.Ascending;
        }

        return (item.Fmt & ListViewInterop.HDF_SORTDOWN) != 0
            ? NativeSortOrder.Descending
            : NativeSortOrder.None;
    }

    internal void UpdateSortIndicator(int index, NativeSortOrder order) {
        if (_listHandle == IntPtr.Zero || index < 0) {
            return;
        }

        var header = ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        if (header == IntPtr.Zero) {
            return;
        }

        var item = new ListViewInterop.HDITEMW { Mask = ListViewInterop.HDI_FORMAT };
        ListViewInterop.SendMessageW(header, ListViewInterop.HDM_GETITEMW, index, ref item);

        item.Fmt &= ~(ListViewInterop.HDF_SORTUP | ListViewInterop.HDF_SORTDOWN);
        item.Fmt |= order switch {
            NativeSortOrder.Ascending => ListViewInterop.HDF_SORTUP,
            NativeSortOrder.Descending => ListViewInterop.HDF_SORTDOWN,
            _ => 0,
        };

        ListViewInterop.SendMessageW(header, ListViewInterop.HDM_SETITEMW, index, ref item);
    }

    internal void InsertItemNative(int index, NativeListViewItem item) {
        if (_listHandle == IntPtr.Zero) {
            return;
        }

        var buffer = Marshal.StringToCoTaskMemUni(item.Cells.Count > 0 ? item.Cells[0] : string.Empty);
        try {
            var native = new ListViewInterop.LVITEMW {
                Mask = ListViewInterop.LVIF_TEXT,
                Item = index,
                Text = buffer,
            };

            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_INSERTITEMW, IntPtr.Zero, ref native);
        } finally {
            Marshal.FreeCoTaskMem(buffer);
        }

        for (var cell = 1; cell < item.Cells.Count; cell++) {
            UpdateCell(index, cell, item.Cells[cell]);
        }
    }

    internal void RemoveItemNative(int index) {
        if (_listHandle != IntPtr.Zero) {
            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_DELETEITEM, index, IntPtr.Zero);
        }
    }

    internal void ClearItemsNative() {
        if (_listHandle != IntPtr.Zero) {
            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_DELETEALLITEMS, IntPtr.Zero, IntPtr.Zero);
        }
    }

    internal void InsertColumnNative(int index, NativeListViewColumn column) {
        if (_listHandle == IntPtr.Zero) {
            return;
        }

        var buffer = Marshal.StringToCoTaskMemUni(column.Text);
        try {
            var native = new ListViewInterop.LVCOLUMNW {
                Mask = ListViewInterop.LVCF_TEXT | ListViewInterop.LVCF_WIDTH |
                    ListViewInterop.LVCF_SUBITEM | ListViewInterop.LVCF_FMT,
                Text = buffer,
                Cx = column.InitialWidth,
                SubItem = index,
                Fmt = ToColumnFormat(column.Alignment),
            };

            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_INSERTCOLUMNW, index, ref native);
        } finally {
            Marshal.FreeCoTaskMem(buffer);
        }

        // The auto-size widths are not meaningful at insert time; they are an instruction to
        // the control, issued once the column exists and has something to measure.
        if (column.InitialWidth < 0) {
            SetColumnWidth(index, column.InitialWidth);
        }
    }

    internal void RemoveColumnNative(int index) {
        if (_listHandle != IntPtr.Zero) {
            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_DELETECOLUMN, index, IntPtr.Zero);
        }
    }

    internal void Reindex() {
        for (var i = 0; i < _items.Count; i++) {
            _items[i].Index = i;
        }

        for (var i = 0; i < _columns.Count; i++) {
            _columns[i].Index = i;
        }
    }

    // --- The list window ------------------------------------------------------------------

    private void CreateListWindow() {
        EnsureCommonControls();

        var style = ListViewInterop.WS_CHILD | ListViewInterop.WS_VISIBLE |
            ListViewInterop.LVS_REPORT | ListViewInterop.LVS_SHOWSELALWAYS;

        if (!_multiSelect) {
            style |= ListViewInterop.LVS_SINGLESEL;
        }

        if (_borderStyle == BorderStyle.FixedSingle) {
            style |= ListViewInterop.WS_BORDER;
        }

        // WS_EX_LAYOUTRTL is the only way a common control mirrors, and it is fixed at creation.
        var exStyle = RightToLeft == RightToLeft.Yes ? ListViewInterop.WS_EX_LAYOUTRTL : 0;
        if (_borderStyle == BorderStyle.Fixed3D) {
            exStyle |= ListViewInterop.WS_EX_CLIENTEDGE;
        }

        _listHandle = ListViewInterop.CreateWindowExW(
            exStyle, ListViewInterop.WindowClass, null, style,
            0, 0, ClientSize.Width, ClientSize.Height,
            Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_listHandle == IntPtr.Zero) {
            throw new InvalidOperationException(
                $"Could not create the {ListViewInterop.WindowClass} window.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }

        ListViewInterop.SendMessageW(
            _listHandle, ListViewInterop.LVM_SETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero,
            // LABELTIP shows the full text of a row too narrow to display it, on hover. That is
            // for the sighted reader of a list whose columns rarely fit.
            (IntPtr)(ListViewInterop.LVS_EX_FULLROWSELECT | ListViewInterop.LVS_EX_DOUBLEBUFFER |
                ListViewInterop.LVS_EX_LABELTIP));

        ApplyWindowTheme();
        ApplyFont();
        ApplyColors();
        ApplyAccessibleName();

        _childSubclass = new ChildMessageFilter(this);
        _childSubclass.AssignHandle(_listHandle);

        UpdateDropTarget();

        Reindex();
        foreach (var column in _columns) {
            InsertColumnNative(column.Index, column);
        }

        foreach (var item in _items) {
            InsertItemNative(item.Index, item);
        }

        // Selection made before the window existed, or carried across a recreation.
        foreach (var item in _items) {
            if (item.PendingSelected) {
                SetSelected(item.Index, value: true);
            }

            if (item.PendingFocused) {
                SetFocused(item.Index, value: true);
            }
        }
    }

    private void DestroyListWindow() {
        // Read the live state back into the model before the window that holds it goes away,
        // so a recreation puts the user back on the row they were on.
        if (_listHandle != IntPtr.Zero) {
            foreach (var item in _items) {
                item.PendingSelected = IsSelected(item.Index);
                item.PendingFocused = IsFocused(item.Index);
            }
        }

        if (_dropTarget is not null && _listHandle != IntPtr.Zero) {
            // A failure here means it was not registered, which is exactly what we want.
            _ = ListViewInterop.RevokeDragDrop(_listHandle);
            _dropTarget = null;
        }

        _childSubclass?.ReleaseHandle();
        _childSubclass = null;

        if (_listHandle != IntPtr.Zero) {
            ListViewInterop.DestroyWindow(_listHandle);
            _listHandle = IntPtr.Zero;
        }

        if (_fontHandle != IntPtr.Zero) {
            ListViewInterop.DeleteObject(_fontHandle);
            _fontHandle = IntPtr.Zero;
        }
    }

    private void RecreateListWindow() {
        DestroyListWindow();
        CreateListWindow();
    }

    private void ApplyFont() {
        if (_listHandle == IntPtr.Zero) {
            return;
        }

        var previous = _fontHandle;
        _fontHandle = Font.ToHfont();
        ListViewInterop.SendMessageW(_listHandle, ListViewInterop.WM_SETFONT, _fontHandle, 1);

        // Only after the control has stopped using it.
        if (previous != IntPtr.Zero) {
            ListViewInterop.DeleteObject(previous);
        }
    }

    private void ApplyAccessibleName() {
        if (_listHandle != IntPtr.Zero) {
            // The MSAA proxy for a list view reports the window text as the control's name,
            // which is not otherwise reachable on a bare common control. An empty string is
            // pushed too: clearing the name has to reach the window, or a reader goes on
            // announcing the name that was cleared.
            ListViewInterop.SetWindowTextW(_listHandle, AccessibleName ?? string.Empty);
        }
    }

    /// <summary>
    /// Applies the visual style the list draws its parts with, in the variant matching the
    /// application's current color mode.
    /// </summary>
    /// <remarks>
    /// Without this the control keeps its pre-Vista appearance: no hover highlight, a header
    /// that looks like another row, and the older row metrics. It is what a WinForms
    /// <c>ListView</c> does for itself, and the reason one looks current. The
    /// <c>DarkMode_</c> variant is what makes the header, the scroll bars and the hover
    /// highlight dark; colors alone leave those three light on a dark list. A failure only
    /// means the control keeps the classic look, which is not worth failing over.
    /// </remarks>
    private void ApplyWindowTheme() {
        if (_listHandle == IntPtr.Zero) {
            return;
        }

        _ = ListViewInterop.SetWindowTheme(
            _listHandle, Application.IsDarkModeEnabled ? "DarkMode_Explorer" : "Explorer", null);
    }

    /// <summary>
    /// Pushes <see cref="Control.BackColor"/> and <see cref="Control.ForeColor"/> into the
    /// list window, which paints itself and would otherwise ignore both.
    /// </summary>
    private void ApplyColors() {
        if (_listHandle == IntPtr.Zero) {
            return;
        }

        var back = (IntPtr)ToColorRef(BackColor);
        ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETBKCOLOR, IntPtr.Zero, back);
        ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETTEXTBKCOLOR, IntPtr.Zero, back);
        ListViewInterop.SendMessageW(
            _listHandle, ListViewInterop.LVM_SETTEXTCOLOR, IntPtr.Zero, (IntPtr)ToColorRef(ForeColor));

        Invalidate(true);
    }

    private bool HasState(int index, uint state) {
        if (_listHandle == IntPtr.Zero || index < 0) {
            return false;
        }

        var value = (uint)ListViewInterop.SendMessageW(
            _listHandle, ListViewInterop.LVM_GETITEMSTATE, index, (IntPtr)state);

        return (value & state) != 0;
    }

    private void SetState(int index, uint state, bool value) {
        if (_listHandle == IntPtr.Zero || index < 0) {
            return;
        }

        var item = new ListViewInterop.LVITEMW {
            State = value ? state : 0,
            StateMask = state,
        };

        ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETITEMSTATE, index, ref item);
    }

    private bool HandleNotification(IntPtr lParam, out IntPtr result) {
        result = IntPtr.Zero;
        if (lParam == IntPtr.Zero) {
            return false;
        }

        var header = Marshal.PtrToStructure<ListViewInterop.NMHDR>(lParam);
        if (header.HwndFrom != _listHandle && !IsHeaderOf(header.HwndFrom)) {
            return false;
        }

        switch (header.Code) {
            case ListViewInterop.NM_CUSTOMDRAW:
                // Only the list draws rows. The header sends the same notification code with
                // the smaller NMCUSTOMDRAW behind it, and writing a row-shaped structure back
                // over that buffer would run past the end of memory Windows owns.
                return header.HwndFrom == _listHandle && HandleCustomDraw(lParam, out result);

            case ListViewInterop.LVN_ITEMCHANGED: {
                    var info = Marshal.PtrToStructure<ListViewInterop.NMLISTVIEW>(lParam);
                    var wasSelected = (info.OldState & ListViewInterop.LVIS_SELECTED) != 0;
                    var isSelected = (info.NewState & ListViewInterop.LVIS_SELECTED) != 0;

                    // The notification fires for every state bit the control touches; only a
                    // change in the selected bit is a selection change.
                    if (wasSelected != isSelected) {
                        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                    }

                    return false;
                }

            case ListViewInterop.LVN_COLUMNCLICK: {
                    var info = Marshal.PtrToStructure<ListViewInterop.NMLISTVIEW>(lParam);
                    if (info.SubItem >= 0 && info.SubItem < _columns.Count) {
                        ColumnClick?.Invoke(this, new NativeColumnClickEventArgs(_columns[info.SubItem]));
                    }

                    return false;
                }

            case ListViewInterop.LVN_BEGINDRAG: {
                    var info = Marshal.PtrToStructure<ListViewInterop.NMLISTVIEW>(lParam);
                    if (info.Item >= 0 && info.Item < _items.Count) {
                        ItemDrag?.Invoke(this, new NativeListViewItemEventArgs(_items[info.Item]));
                    }

                    return false;
                }

            case ListViewInterop.NM_DBLCLK:
            case ListViewInterop.NM_RETURN: {
                    if (FocusedItem is { } item) {
                        ItemActivate?.Invoke(this, new NativeListViewItemEventArgs(item));
                    }

                    return false;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// Paints a row in its own color. The control has no per-row color of its own, so the
    /// only way to have one is to answer the draw notification and hand back a text color.
    /// </summary>
    private bool HandleCustomDraw(IntPtr lParam, out IntPtr result) {
        result = ListViewInterop.CDRF_DODEFAULT;
        var draw = Marshal.PtrToStructure<ListViewInterop.NMLVCUSTOMDRAW>(lParam);

        switch (draw.Nmcd.DrawStage) {
            case ListViewInterop.CDDS_PREPAINT: {
                    // Ask for callbacks only when there is something to answer with. Otherwise
                    // every row of every repaint would cross into managed code to say "nothing".
                    var flags = ListViewInterop.CDRF_DODEFAULT;

                    if (_items.Exists(item => item.ForeColor is not null)) {
                        flags |= ListViewInterop.CDRF_NOTIFYITEMDRAW;
                    }

                    if (HasInsertionMark) {
                        flags |= ListViewInterop.CDRF_NOTIFYPOSTPAINT;
                    }

                    result = flags;
                    return true;
                }

            case ListViewInterop.CDDS_POSTPAINT:
                DrawInsertionMark(draw.Nmcd.Hdc);
                return true;

            case ListViewInterop.CDDS_ITEMPREPAINT: {
                    // A row index always fits; the field is pointer-sized only because the
                    // structure is shared with notifications that carry a real pointer.
                    var index = (int)draw.Nmcd.ItemSpec.ToInt64();
                    if (index < 0 || index >= _items.Count || _items[index].ForeColor is not { } color) {
                        return true;
                    }

                    draw.ClrText = ToColorRef(color);
                    Marshal.StructureToPtr(draw, lParam, fDeleteOld: false);
                    result = ListViewInterop.CDRF_NEWFONT;
                    return true;
                }

            default:
                return false;
        }
    }

    /// <summary>Whether a drop indicator is set and still points at a row that exists.</summary>
    private bool HasInsertionMark => _insertionIndex >= 0 && _insertionIndex < _items.Count;

    /// <summary>
    /// Draws the drop indicator across the row edge, with the end caps that make it read as a
    /// line between rows rather than an underline belonging to one of them.
    /// </summary>
    private void DrawInsertionMark(IntPtr hdc) {
        if (!HasInsertionMark || hdc == IntPtr.Zero) {
            return;
        }

        var bounds = GetItemBounds(_insertionIndex);
        if (bounds.IsEmpty) {
            return;
        }

        var y = _insertionAfter ? bounds.Bottom : bounds.Top;

        // Scale with the display, and keep the line inside the control so a mark on the last
        // row is not painted half outside it.
        var thickness = Math.Max(2, (int)Math.Round(2 * (DeviceDpi / 96.0)));
        var cap = thickness * 2;
        y = Math.Clamp(y, thickness, Math.Max(thickness, ClientSize.Height - thickness));

        using var graphics = Graphics.FromHdc(hdc);
        using var brush = new SolidBrush(SystemColors.Highlight);

        graphics.FillRectangle(brush, bounds.Left, y - (thickness / 2), bounds.Width, thickness);
        graphics.FillRectangle(brush, bounds.Left, y - cap, thickness, cap * 2);
        graphics.FillRectangle(brush, bounds.Right - thickness, y - cap, thickness, cap * 2);
    }

    /// <summary><c>COLORREF</c> is 0x00BBGGRR, the reverse of the usual order.</summary>
    private static uint ToColorRef(Color color) =>
        (uint)(color.R | (color.G << 8) | (color.B << 16));

    /// <summary>Repaints one row, after something that only changes how it looks.</summary>
    internal void InvalidateRow(int index) {
        if (_listHandle == IntPtr.Zero || index < 0) {
            return;
        }

        var bounds = GetItemBounds(index);
        if (!bounds.IsEmpty) {
            Invalidate(bounds);
        }
    }

    private bool IsHeaderOf(IntPtr candidate) =>
        _listHandle != IntPtr.Zero && candidate ==
            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);

    private static bool _commonControlsReady;

    private static void EnsureCommonControls() {
        if (_commonControlsReady) {
            return;
        }

        var icc = new ListViewInterop.INITCOMMONCONTROLSEX {
            DwSize = (uint)Marshal.SizeOf<ListViewInterop.INITCOMMONCONTROLSEX>(),
            DwICC = ListViewInterop.IccListViewClasses,
        };

        ListViewInterop.InitCommonControlsEx(ref icc);
        _commonControlsReady = true;
    }

    private void UpdateDropTarget() {
        if (_listHandle == IntPtr.Zero) {
            return;
        }

        if (AllowDrop && _dropTarget is null) {
            var target = new ChildDropTarget(this);
            var hr = ListViewInterop.RegisterDragDrop(_listHandle, target);
            if (hr < 0) {
                // Silently accepting no drops would look like a bug in the consumer.
                Marshal.ThrowExceptionForHR(hr);
            }

            _dropTarget = target;
        } else if (!AllowDrop && _dropTarget is not null) {
            _ = ListViewInterop.RevokeDragDrop(_listHandle);
            _dropTarget = null;
        }
    }

    /// <summary>
    /// Turns OLE drop callbacks on the list window into the ordinary WinForms drag events on
    /// this control, so a consumer writes the same handlers it would for any other control.
    /// </summary>
    private sealed class ChildDropTarget(NativeListView owner): ListViewInterop.IOleDropTarget {
        private IDataObject? _data;

        public int OleDragEnter(object dataObject, int keyState, ListViewInterop.POINTL point, ref int effect) {
            _data = Wrap(dataObject);
            var args = Build(keyState, point, effect);
            owner.OnDragEnter(args);
            effect = (int)args.Effect;
            return 0;
        }

        public int OleDragOver(int keyState, ListViewInterop.POINTL point, ref int effect) {
            var args = Build(keyState, point, effect);
            owner.OnDragOver(args);
            effect = (int)args.Effect;
            return 0;
        }

        public int OleDragLeave() {
            owner.OnDragLeave(EventArgs.Empty);
            _data = null;
            return 0;
        }

        public int OleDrop(object dataObject, int keyState, ListViewInterop.POINTL point, ref int effect) {
            _data = Wrap(dataObject) ?? _data;
            var args = Build(keyState, point, effect);
            owner.OnDragDrop(args);
            effect = (int)args.Effect;
            _data = null;
            return 0;
        }

        /// <summary>
        /// The incoming effect is the set the source permits; the handler narrows it to the
        /// one it wants, which is what goes back out.
        /// </summary>
        private DragEventArgs Build(int keyState, ListViewInterop.POINTL point, int effect) =>
            new(_data!, keyState, point.X, point.Y, (DragDropEffects)effect, DragDropEffects.None);

        /// <summary>
        /// Recovers the data object the drag source passed in.
        /// </summary>
        /// <remarks>
        /// Letting the runtime marshal this as an interface is what makes an in-process drag
        /// work: the object that comes back is the very one the source handed to
        /// <c>DoDragDrop</c>, so a payload of any type is simply still there. Taking the raw
        /// pointer and wrapping it instead produces a data object that advertises the right
        /// format and yields nothing from it, because pulling a custom type back out of a
        /// wrapper needs the deserialization .NET no longer performs.
        /// </remarks>
        private static IDataObject? Wrap(object? value) => value switch {
            null => null,
            IDataObject managed => managed,
            _ => new DataObject(value),
        };
    }

    /// <summary>
    /// Subclasses the list window for the things the container cannot see: keystrokes, which go
    /// to whichever window has focus, and Tab, which no one else will move along because the
    /// list is not a WinForms control.
    /// </summary>
    private sealed class ChildMessageFilter(NativeListView owner): NativeWindow {
        private const int VK_TAB = 0x09;

        protected override void WndProc(ref Message m) {
            if (m.Msg == (int)ListViewInterop.WM_KEYDOWN) {
                var key = (Keys)(int)m.WParam | Control.ModifierKeys;

                if ((int)m.WParam == VK_TAB) {
                    var forward = (Control.ModifierKeys & Keys.Shift) == 0;
                    owner.Parent?.SelectNextControl(owner, forward, tabStopOnly: true, nested: true, wrap: true);
                    return;
                }

                var args = new KeyEventArgs(key);
                owner.OnKeyDownFromList(args);
                if (args.Handled) {
                    return;
                }
            }

            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// Raises <see cref="Control.KeyDown"/> for a keystroke that landed on the list window.
    /// </summary>
    private void OnKeyDownFromList(KeyEventArgs e) => OnKeyDown(e);

    /// <summary>The rows, kept in step with the list window.</summary>
    private sealed class ItemCollection(NativeListView owner, IList<NativeListViewItem> items)
        : Collection<NativeListViewItem>(items) {
        protected override void InsertItem(int index, NativeListViewItem item) {
            ArgumentNullException.ThrowIfNull(item);
            base.InsertItem(index, item);
            item.ListView = owner;
            owner.Reindex();
            owner.InsertItemNative(index, item);
        }

        protected override void SetItem(int index, NativeListViewItem item) {
            ArgumentNullException.ThrowIfNull(item);
            this[index].ListView = null;
            base.SetItem(index, item);
            item.ListView = owner;
            owner.Reindex();
            owner.RemoveItemNative(index);
            owner.InsertItemNative(index, item);
        }

        protected override void RemoveItem(int index) {
            this[index].ListView = null;
            this[index].Index = -1;
            base.RemoveItem(index);
            owner.RemoveItemNative(index);
            owner.Reindex();
        }

        protected override void ClearItems() {
            foreach (var item in this) {
                item.ListView = null;
                item.Index = -1;
            }

            base.ClearItems();
            owner.ClearItemsNative();
        }
    }

    /// <summary>The columns, kept in step with the list window.</summary>
    private sealed class ColumnCollection(NativeListView owner, IList<NativeListViewColumn> columns)
        : Collection<NativeListViewColumn>(columns) {
        protected override void InsertItem(int index, NativeListViewColumn column) {
            ArgumentNullException.ThrowIfNull(column);
            base.InsertItem(index, column);
            column.ListView = owner;
            owner.Reindex();
            owner.InsertColumnNative(index, column);
        }

        protected override void SetItem(int index, NativeListViewColumn column) {
            ArgumentNullException.ThrowIfNull(column);
            this[index].ListView = null;
            base.SetItem(index, column);
            column.ListView = owner;
            owner.Reindex();
            owner.RemoveColumnNative(index);
            owner.InsertColumnNative(index, column);
        }

        protected override void RemoveItem(int index) {
            this[index].ListView = null;
            this[index].Index = -1;
            base.RemoveItem(index);
            owner.RemoveColumnNative(index);
            owner.Reindex();
        }

        protected override void ClearItems() {
            for (var i = Count - 1; i >= 0; i--) {
                this[i].ListView = null;
                this[i].Index = -1;
                owner.RemoveColumnNative(i);
            }

            base.ClearItems();
        }
    }
}

/// <summary>Carries the row an event concerns.</summary>
/// <param name="item">The row.</param>
public sealed class NativeListViewItemEventArgs(NativeListViewItem item): EventArgs {
    /// <summary>The row the event concerns.</summary>
    public NativeListViewItem Item { get; } = item;
}

/// <summary>Carries the column whose header was clicked.</summary>
/// <param name="column">The column.</param>
public sealed class NativeColumnClickEventArgs(NativeListViewColumn column): EventArgs {
    /// <summary>The column whose header was clicked.</summary>
    public NativeListViewColumn Column { get; } = column;
}
