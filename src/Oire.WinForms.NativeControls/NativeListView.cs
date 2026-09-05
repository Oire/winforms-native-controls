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
[DesignerCategory("Code")]
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
    private int _lastSelectedIndex = -1;

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
    /// </remarks>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
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
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IntPtr ListHandle => _listHandle;

    /// <summary>Raised when the set of selected rows changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised when a column header is clicked.</summary>
    public event EventHandler<NativeColumnClickEventArgs>? ColumnClick;

    /// <summary>Raised on double-click or Enter — the row the user means to open.</summary>
    public event EventHandler<NativeListViewItemEventArgs>? ItemActivate;

    /// <summary>Raised when the user starts dragging a row.</summary>
    public event EventHandler<NativeListViewItemEventArgs>? ItemDrag;

    /// <summary>Scrolls the row at <paramref name="index"/> into view.</summary>
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

    /// <summary>The row at a point in this control's client coordinates, or null.</summary>
    public NativeListViewItem? GetItemAt(int x, int y) => GetItemAt(new Point(x, y));

    /// <summary>The row at a point in this control's client coordinates, or null.</summary>
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
    /// Draws the insertion mark before or after a row — the drop indicator for a reorder.
    /// </summary>
    public void SetInsertionMark(int index, bool after) {
        if (_listHandle == IntPtr.Zero) {
            return;
        }

        var mark = new ListViewInterop.LVINSERTMARK {
            CbSize = (uint)Marshal.SizeOf<ListViewInterop.LVINSERTMARK>(),
            DwFlags = after ? ListViewInterop.LVIM_AFTER : 0,
            Item = index,
        };

        ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETINSERTMARK, IntPtr.Zero, ref mark);
    }

    /// <summary>Removes the insertion mark.</summary>
    public void ClearInsertionMark() => SetInsertionMark(-1, after: false);

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
        // a screen reader must land on.
        if (_listHandle != IntPtr.Zero) {
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
        ApplyFont();
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

    internal int GetColumnWidth(int index) =>
        _listHandle == IntPtr.Zero || index < 0
            ? 0
            : (int)ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_GETCOLUMNWIDTH, index, IntPtr.Zero);

    internal void SetColumnWidth(int index, int width) {
        if (_listHandle != IntPtr.Zero && index >= 0) {
            ListViewInterop.SendMessageW(_listHandle, ListViewInterop.LVM_SETCOLUMNWIDTH, index, width);
        }
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

        var buffer = Marshal.StringToCoTaskMemUni(item.Cells.Count > 0 ? item.Cells[0] : String.Empty);
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
                Fmt = column.Alignment switch {
                    NativeColumnAlignment.Right => ListViewInterop.LVCFMT_RIGHT,
                    NativeColumnAlignment.Center => ListViewInterop.LVCFMT_CENTER,
                    _ => ListViewInterop.LVCFMT_LEFT,
                },
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

        // WS_EX_LAYOUTRTL is the only way a common control mirrors, and it is fixed at creation.
        var exStyle = RightToLeft == RightToLeft.Yes ? ListViewInterop.WS_EX_LAYOUTRTL : 0;

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
            (IntPtr)(ListViewInterop.LVS_EX_FULLROWSELECT | ListViewInterop.LVS_EX_DOUBLEBUFFER |
                ListViewInterop.LVS_EX_LABELTIP));

        ApplyFont();
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
        if (_listHandle != IntPtr.Zero && !String.IsNullOrEmpty(AccessibleName)) {
            // The MSAA proxy for a list view reports the window text as the control's name,
            // which is not otherwise reachable on a bare common control.
            ListViewInterop.SetWindowTextW(_listHandle, AccessibleName);
        }
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
                return HandleCustomDraw(lParam, out result);

            case ListViewInterop.LVN_ITEMCHANGED: {
                    var info = Marshal.PtrToStructure<ListViewInterop.NMLISTVIEW>(lParam);
                    var wasSelected = (info.OldState & ListViewInterop.LVIS_SELECTED) != 0;
                    var isSelected = (info.NewState & ListViewInterop.LVIS_SELECTED) != 0;
                    if (wasSelected != isSelected) {
                        var focused = FocusedItem?.Index ?? -1;
                        if (focused != _lastSelectedIndex || !isSelected) {
                            _lastSelectedIndex = focused;
                        }

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
            case ListViewInterop.CDDS_PREPAINT:
                // Nothing to say yet; ask to be called again per row.
                result = ListViewInterop.CDRF_NOTIFYITEMDRAW;
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

        public int OleDragEnter(IntPtr dataObject, int keyState, ListViewInterop.POINTL point, ref int effect) {
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

        public int OleDrop(IntPtr dataObject, int keyState, ListViewInterop.POINTL point, ref int effect) {
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

        private static IDataObject? Wrap(IntPtr unknown) {
            if (unknown == IntPtr.Zero) {
                return null;
            }

            var value = Marshal.GetObjectForIUnknown(unknown);
            return value as IDataObject ?? new DataObject(value);
        }
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
