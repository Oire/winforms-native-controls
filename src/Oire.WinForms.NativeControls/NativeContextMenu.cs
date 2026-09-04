using System.Diagnostics.CodeAnalysis;

namespace Oire.WinForms.NativeControls;

/// <summary>
/// Context-menu invocation details handed to a <see cref="NativeContextMenu.Resolver"/>.
/// </summary>
/// <param name="Control">The control the menu was invoked on.</param>
/// <param name="ScreenLocation">Anchor in screen coordinates.</param>
/// <param name="FromKeyboard">
/// True for Shift+F10 or the Applications key, false for a right-click or touch long-press.
/// Callers that hit-test a sub-region (a ListView column header, say) should skip the test
/// when this is true — a keyboard invocation carries no meaningful pointer position.
/// </param>
public sealed record ContextMenuRequest(Control Control, Point ScreenLocation, bool FromKeyboard);

/// <summary>
/// A native popup menu, optionally bound to a control so it opens on right-click, Shift+F10,
/// the Applications key, and touch long-press.
/// </summary>
/// <remarks>
/// Binding listens for <c>WM_CONTEXTMENU</c>, which is the one Win32 signal all of those
/// gestures funnel through — so the keyboard path is not a bolted-on extra, it is the same
/// code path as the mouse.
/// </remarks>
public sealed class NativeContextMenu: IDisposable {
    private NativeMenuSpec _spec;
    private NativeMenuTree _tree;
    private Control? _control;
    private ContextMenuWindow? _subclass;
    private bool _rightToLeft;
    private bool _disposed;

    /// <summary>Builds the popup from <paramref name="spec"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The spec has a mnemonic collision or a malformed radio group.
    /// </exception>
    public NativeContextMenu(NativeMenuSpec spec) {
        ArgumentNullException.ThrowIfNull(spec);
        MenuSpecValidator.Validate(spec);
        _spec = spec;
        _tree = NativeMenuTree.Build(spec, isMenuBar: false, rightToLeft: false);
    }

    /// <summary>
    /// Optional hook that picks which menu to open for a given invocation, letting one control
    /// carry more than one context menu. Returning null suppresses the menu; leaving this null
    /// always opens this menu.
    /// </summary>
    public Func<ContextMenuRequest, NativeContextMenu?>? Resolver { get; set; }

    /// <summary>
    /// Binds the menu to <paramref name="control"/>. One instance serves one control — give
    /// each control its own instance, built from the same spec if they share a menu.
    /// </summary>
    public void AttachTo(Control control) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(control);

        if (_control is not null) {
            throw new InvalidOperationException(
                "This context menu is already attached; create one instance per control.");
        }

        _control = control;
        EnsureRightToLeft(control);

        _subclass = new ContextMenuWindow(this);
        if (control.IsHandleCreated) {
            _subclass.AssignHandle(control.Handle);
        }

        // Subscribed unconditionally: a control can recreate its HWND at any point, and the
        // subclass has to follow it there.
        control.HandleCreated += OnControlHandleCreated;
        control.HandleDestroyed += OnControlHandleDestroyed;
    }

    /// <summary>
    /// Opens the menu at <paramref name="screenLocation"/> and runs the chosen item's callback.
    /// Blocks until the user picks an item or dismisses the menu, as Win32 popup tracking does.
    /// </summary>
    public void Show(Control owner, Point screenLocation) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(owner);

        EnsureRightToLeft(owner);

        // Refresh enabled and checked state up front rather than leaning on WM_INITMENUPOPUP,
        // so the menu is correct even when it is shown on a control this instance never
        // subclassed.
        _tree.PushAllState();

        const uint flags = Win32Interop.TPM_RETURNCMD | Win32Interop.TPM_LEFTALIGN |
            Win32Interop.TPM_TOPALIGN | Win32Interop.TPM_RIGHTBUTTON;

        int command;
        using (MenuTrackingScope.Enter()) {
            command = Win32Interop.TrackPopupMenuEx(
                _tree.Handle, flags, screenLocation.X, screenLocation.Y, owner.Handle, IntPtr.Zero);
        }

        if (command == 0) {
            return;
        }

        var callback = _tree.Resolve((ushort)command);
        if (callback is not null && owner.IsHandleCreated) {
            // Queue rather than run inline: TrackPopupMenuEx has only just unwound its nested
            // message loop, and a dialog opened from here would otherwise nest inside it.
            owner.BeginInvoke(callback);
        }
    }

    /// <summary>
    /// Swaps in a freshly built menu — used after a language change.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A popup menu is currently being tracked, or the new spec fails validation.
    /// </exception>
    public void Rebuild(NativeMenuSpec spec) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(spec);

        if (MenuTrackingScope.IsTracking) {
            throw new InvalidOperationException("Cannot rebuild a context menu while a popup menu is open.");
        }

        MenuSpecValidator.Validate(spec);

        var newTree = NativeMenuTree.Build(spec, isMenuBar: false, rightToLeft: _rightToLeft);
        var oldTree = _tree;
        _spec = spec;
        _tree = newTree;
        oldTree.Dispose();
    }

    /// <summary>
    /// Test seam: maps a command id to its callback without invoking it, so tests can verify
    /// id allocation and rebuild behavior.
    /// </summary>
    internal bool TryRoute(ushort id, [NotNullWhen(true)] out Action? callback) {
        callback = null;
        if (!_tree.TryGetCommand(id, out var command)) {
            return false;
        }

        callback = command.Spec.OnClick;
        return callback is not null;
    }

    /// <summary>Test seam: the command ids currently allocated, in ascending order.</summary>
    internal IReadOnlyCollection<ushort> CommandIds => _tree.Commands.Keys.Order().ToArray();

    /// <summary>
    /// Releases the popup menu and detaches from the control it was bound to.
    /// Safe to call more than once, and safe on a partially initialized instance.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;

        if (_control is not null) {
            _control.HandleCreated -= OnControlHandleCreated;
            _control.HandleDestroyed -= OnControlHandleDestroyed;
            _control = null;
        }

        _subclass?.ReleaseHandle();
        _subclass = null;
        _tree.Dispose();
    }

    /// <summary>
    /// Decodes a <c>WM_CONTEXTMENU</c> anchor and opens the appropriate menu. Returns true
    /// when the message was handled here.
    /// </summary>
    internal bool HandleContextMenu(IntPtr lParam) {
        if (_control is null || _disposed) {
            return false;
        }

        var raw = lParam.ToInt64();

        // Keyboard invocation (Shift+F10, Applications key) reports -1 rather than a position.
        var fromKeyboard = unchecked((int)raw) == -1;
        var anchor = fromKeyboard
            ? KeyboardAnchorFor(_control)
            : new Point(unchecked((short)(raw & 0xFFFF)), unchecked((short)((raw >> 16) & 0xFFFF)));

        var target = Resolver is null
            ? this
            : Resolver(new ContextMenuRequest(_control, anchor, fromKeyboard));

        // A null resolver result means "show nothing", but the message is still ours.
        target?.Show(_control, anchor);
        return true;
    }

    /// <summary>
    /// Where a keyboard-invoked menu should appear. Anchoring at the focused row rather than
    /// the control's corner keeps the visual menu next to what a screen-reader user is on,
    /// which matters when a sighted colleague is looking over their shoulder.
    /// </summary>
    private static Point KeyboardAnchorFor(Control control) {
        switch (control) {
            case ListView { FocusedItem: { } item } listView:
                return listView.PointToScreen(new Point(item.Bounds.Left, item.Bounds.Bottom));
            case TreeView { SelectedNode: { } node } treeView:
                return treeView.PointToScreen(new Point(node.Bounds.Left, node.Bounds.Bottom));
            case ListBox { SelectedIndex: >= 0 } listBox: {
                    var bounds = listBox.GetItemRectangle(listBox.SelectedIndex);
                    return listBox.PointToScreen(new Point(bounds.Left, bounds.Bottom));
                }
            default:
                return control.PointToScreen(Point.Empty);
        }
    }

    /// <summary>
    /// Rebuilds the menu when the owning control's reading direction differs from what the
    /// current <c>HMENU</c> was built for. <c>WS_EX_LAYOUTRTL</c> mirrors a menu bar on its
    /// own, but popups need the per-item <c>MFT_RIGHTORDER</c> / <c>MFT_RIGHTJUSTIFY</c> flags
    /// baked in at build time.
    /// </summary>
    private void EnsureRightToLeft(Control control) {
        var rightToLeft = control.RightToLeft == RightToLeft.Yes;
        if (rightToLeft == _rightToLeft) {
            return;
        }

        _rightToLeft = rightToLeft;
        var rebuilt = NativeMenuTree.Build(_spec, isMenuBar: false, rightToLeft);
        var oldTree = _tree;
        _tree = rebuilt;
        oldTree.Dispose();
    }

    private void OnControlHandleCreated(object? sender, EventArgs e) {
        if (_subclass is not null && _control is not null && _subclass.Handle == IntPtr.Zero) {
            _subclass.AssignHandle(_control.Handle);
        }
    }

    private void OnControlHandleDestroyed(object? sender, EventArgs e) => _subclass?.ReleaseHandle();

    /// <summary>Subclasses the owning control to catch <c>WM_CONTEXTMENU</c>.</summary>
    private sealed class ContextMenuWindow(NativeContextMenu owner): NativeWindow {
        protected override void WndProc(ref Message m) {
            if (m.Msg == Win32Interop.WM_CONTEXTMENU && owner.HandleContextMenu(m.LParam)) {
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == Win32Interop.WM_INITMENUPOPUP) {
                owner._tree.PushState(m.WParam);
            }

            base.WndProc(ref m);
        }
    }
}
