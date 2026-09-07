using System.Diagnostics.CodeAnalysis;

namespace Oire.WinForms.NativeControls;

/// <summary>
/// Owns a form's native menu bar: the <c>HMENU</c> tree, the accelerator table that fires its
/// shortcuts from anywhere in the form, and the window subclass that routes <c>WM_COMMAND</c>
/// and <c>WM_INITMENUPOPUP</c> back into the spec and watches the menu loop open and close.
/// </summary>
/// <remarks>
/// A native menu bar exists because JAWS announces one correctly as a menu bar, with real
/// submenus; WinForms' <c>MenuStrip</c> announces generically and renders submenus poorly.
/// </remarks>
public sealed class NativeMenuBar: IDisposable {
    private readonly Form _form;
    private NativeMenuTree? _tree;
    private IntPtr _accelerators;
    private MenuMessageFilter? _subclass;
    private AcceleratorFilter? _acceleratorFilter;
    private bool _commandChosen;
    private IntPtr _focusAtMenuExit;
    private bool _disposed;

    /// <summary>Creates a menu bar owner for <paramref name="form"/>. Nothing is built until <see cref="Attach"/>.</summary>
    /// <param name="form">The form whose menu bar this will own.</param>
    public NativeMenuBar(Form form) {
        ArgumentNullException.ThrowIfNull(form);
        _form = form;
    }

    /// <summary>
    /// Validates <paramref name="spec"/>, builds the menu, registers its accelerators, and
    /// puts it on the form. Calling this again on the same HWND is equivalent to
    /// <see cref="Rebuild"/>; calling it after a handle recreation rebinds to the new HWND.
    /// </summary>
    /// <param name="spec">The menu description to build the bar from.</param>
    /// <exception cref="ArgumentException">
    /// The spec has a mnemonic collision or a malformed radio group. Nothing is allocated in
    /// that case — validation runs before the first <c>HMENU</c>.
    /// </exception>
    public void Attach(NativeMenuSpec spec) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(spec);

        // A Form can recreate its HWND (a RightToLeftLayout flip, for one). The old subclass
        // and the SetMenu binding died with the old handle, so that case needs a full attach.
        var boundToCurrentHandle = _subclass is not null && _subclass.Handle == _form.Handle;

        if (_tree is not null && boundToCurrentHandle) {
            Rebuild(spec);
            return;
        }

        if (_tree is not null) {
            ReleaseNativeResources();
        }

        MenuSpecValidator.Validate(spec);

        // WS_EX_LAYOUTRTL mirrors the menu bar itself, but a dropdown is its own window and
        // needs the per-item flags — without them submenus open leftwards on an RTL layout,
        // and the arrow keys that walk into them stay reversed.
        var tree = NativeMenuTree.Build(spec, isMenuBar: true, rightToLeft: IsRightToLeft);
        var accelerators = CreateAccelerators(tree);

        _tree = tree;
        _accelerators = accelerators;

        Win32Interop.SetMenu(_form.Handle, tree.Handle);
        Win32Interop.DrawMenuBar(_form.Handle);

        _subclass = new MenuMessageFilter(this);
        _subclass.AssignHandle(_form.Handle);

        _acceleratorFilter = new AcceleratorFilter(this);
        Application.AddMessageFilter(_acceleratorFilter);
    }

    /// <summary>
    /// Swaps in a freshly built menu — used after a language change, where every label needs
    /// to be re-evaluated against the new catalog.
    /// </summary>
    /// <param name="spec">The replacement menu description.</param>
    /// <exception cref="InvalidOperationException">
    /// A popup menu is currently being tracked, so its <c>HMENU</c> cannot be destroyed yet.
    /// </exception>
    /// <exception cref="ArgumentException">The new spec fails validation.</exception>
    public void Rebuild(NativeMenuSpec spec) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(spec);

        if (_tree is null) {
            Attach(spec);
            return;
        }

        if (MenuTrackingScope.IsTracking) {
            throw new InvalidOperationException("Cannot rebuild the menu bar while a popup menu is open.");
        }

        MenuSpecValidator.Validate(spec);

        var newTree = NativeMenuTree.Build(spec, isMenuBar: true, rightToLeft: IsRightToLeft);
        IntPtr newAccelerators;
        try {
            newAccelerators = CreateAccelerators(newTree);
        } catch {
            newTree.Dispose();
            throw;
        }

        var oldTree = _tree;
        var oldAccelerators = _accelerators;

        _tree = newTree;
        _accelerators = newAccelerators;

        // Hand the new menu to the form before destroying the old one, so the old HMENU is
        // no longer in use by the time DestroyMenu runs.
        Win32Interop.SetMenu(_form.Handle, newTree.Handle);
        Win32Interop.DrawMenuBar(_form.Handle);

        oldTree.Dispose();
        DestroyAccelerators(oldAccelerators);
    }

    /// <summary>
    /// Whether the owning form lays out right to left. Read fresh on every build, so a
    /// language switch that flips the form also flips the menus.
    /// </summary>
    private bool IsRightToLeft => _form.RightToLeft == RightToLeft.Yes;

    /// <summary>
    /// Test seam: maps a command id to its callback without invoking it and without the
    /// enabled-state gate, so tests can verify id allocation and rebuild behavior.
    /// </summary>
    internal bool TryRoute(ushort id, [NotNullWhen(true)] out Action? callback) {
        callback = null;
        if (_tree is null || !_tree.TryGetCommand(id, out var command)) {
            return false;
        }

        callback = command.Spec.OnClick;
        return callback is not null;
    }

    /// <summary>Test seam: the command ids currently allocated, in ascending order.</summary>
    internal IReadOnlyCollection<ushort> CommandIds =>
        _tree is null ? [] : _tree.Commands.Keys.Order().ToArray();

    /// <summary>
    /// Runs the item behind a chosen command id. Returns true when the id belongs to this
    /// menu — including when the item turned out to be disabled, so a stale accelerator is
    /// swallowed rather than passed on.
    /// </summary>
    internal bool TryDispatch(ushort id) {
        if (_tree is null || !_tree.TryGetCommand(id, out _)) {
            return false;
        }

        _commandChosen = true;

        var callback = _tree.Resolve(id);
        if (callback is not null && _form.IsHandleCreated) {
            _form.BeginInvoke(callback);
        }

        return true;
    }

    /// <summary>
    /// Releases the menu, the accelerator table, the window subclass and the message filter.
    /// Safe to call more than once, and safe on a partially initialized instance.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        ReleaseNativeResources();
    }

    /// <summary>
    /// Releases the message filter, the window subclass, the <c>HMENU</c> and the
    /// <c>HACCEL</c>. Everything here needs the form's HWND to still exist, so it must run
    /// before the form tears its handle down.
    /// </summary>
    private void ReleaseNativeResources() {
        if (_acceleratorFilter is not null) {
            Application.RemoveMessageFilter(_acceleratorFilter);
            _acceleratorFilter = null;
        }

        _subclass?.ReleaseHandle();
        _subclass = null;

        if (_tree is not null && _form.IsHandleCreated) {
            Win32Interop.SetMenu(_form.Handle, IntPtr.Zero);
        }

        _tree?.Dispose();
        _tree = null;

        DestroyAccelerators(_accelerators);
        _accelerators = IntPtr.Zero;
    }

    private static IntPtr CreateAccelerators(NativeMenuTree tree) {
        var entries = tree.Commands
            .Where(pair => pair.Value.Spec.ShortcutKeys is not null)
            .Select(pair => {
                var (fVirt, key) = AccelConverter.ConvertKey(pair.Value.Spec.ShortcutKeys!.Value);
                return new Win32Interop.ACCEL { FVirt = fVirt, Key = key, Cmd = pair.Key };
            })
            .ToArray();

        return entries.Length == 0 ? IntPtr.Zero : Win32Interop.CreateAcceleratorTableW(entries, entries.Length);
    }

    private static void DestroyAccelerators(IntPtr accelerators) {
        if (accelerators != IntPtr.Zero) {
            Win32Interop.DestroyAcceleratorTable(accelerators);
        }
    }

    /// <summary>
    /// Subclasses the form's HWND to catch what a native menu needs: the command the user
    /// chose, the request to refresh a popup's state just before it is shown, and the menu
    /// loop opening and closing.
    /// </summary>
    private sealed class MenuMessageFilter(NativeMenuBar owner): NativeWindow {
        /// <summary>
        /// Bounced to self after a menu closes, twice, to reach the far side of the
        /// <c>WM_COMMAND</c> a chosen item posts as the menu loop unwinds. Posted messages are
        /// FIFO, so the second bounce is guaranteed to land after it; <c>BeginInvoke</c> is not
        /// usable here because WinForms drains its whole callback queue inside one message.
        /// </summary>
        private static readonly uint AnnounceFocusMessage =
            Win32Interop.RegisterWindowMessageW("Oire.WinForms.NativeControls.AnnounceFocus");

        protected override void WndProc(ref Message m) {
            if (m.Msg == (int)AnnounceFocusMessage && AnnounceFocusMessage != 0) {
                if (m.WParam == IntPtr.Zero) {
                    Win32Interop.PostMessageW(m.HWnd, AnnounceFocusMessage, 1, IntPtr.Zero);
                } else if (!owner._commandChosen) {
                    FocusAnnouncer.Announce(owner._focusAtMenuExit);
                }

                return;
            }

            switch (m.Msg) {
                case Win32Interop.WM_COMMAND:
                    // lParam is zero for menu and accelerator commands, and the child HWND for
                    // control notifications — which belong to WinForms, not to us.
                    if (m.LParam == IntPtr.Zero && owner.TryDispatch((ushort)(m.WParam.ToInt64() & 0xFFFF))) {
                        m.Result = IntPtr.Zero;
                        return;
                    }

                    break;

                case Win32Interop.WM_INITMENUPOPUP:
                    owner._tree?.PushState(m.WParam);
                    break;

                case Win32Interop.WM_ENTERMENULOOP:
                    owner._commandChosen = false;
                    break;

                case Win32Interop.WM_EXITMENULOOP:
                    // Dismissing a menu moves no focus and so announces nothing. Say where the
                    // user ended up — but not yet: a chosen item's WM_COMMAND has not arrived.
                    owner._focusAtMenuExit = Win32Interop.GetFocus();
                    Win32Interop.PostMessageW(m.HWnd, AnnounceFocusMessage, IntPtr.Zero, IntPtr.Zero);
                    break;

                // WM_MENUCHAR is deliberately left to Windows: MenuSpecValidator guarantees
                // unique mnemonics per level, so the default first-character fallback is fine.
                default:
                    break;
            }

            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// Feeds keyboard messages through the accelerator table before WinForms sees them, so a
    /// menu shortcut fires from any focus inside the form — including while the menu is open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A message filter is application-wide, so the filter has to decide for itself whether a
    /// given keystroke belongs to its form. It asks the only question that actually settles
    /// that: is the window the message is aimed at this form, or something inside it? Every
    /// control on the form — including the <c>SysListView32</c> that
    /// <see cref="NativeListView"/> creates, which is not a WinForms control at all — is a
    /// descendant of the form's HWND, and nothing outside the form is.
    /// </para>
    /// <para>
    /// This deliberately does not consult <see cref="Form.ActiveForm"/>. That property answers
    /// a different question and gets this one wrong in two shipping cases: for an MDI child it
    /// names the MDI <em>parent</em>, so a child's own menu bar would never fire; and it is
    /// null whenever the active window is not a WinForms <c>Form</c>, which is the normal state
    /// of a mixed WPF or native host. Owned top-level windows are excluded for free, because a
    /// dialog is owned by the form but is not a child of it.
    /// </para>
    /// </remarks>
    private sealed class AcceleratorFilter(NativeMenuBar owner): IMessageFilter {
        private const int WM_KEYFIRST = 0x0100;
        private const int WM_KEYLAST = 0x0109;

        public bool PreFilterMessage(ref Message m) {
            if (m.Msg is < WM_KEYFIRST or > WM_KEYLAST) {
                return false;
            }

            if (owner._disposed || owner._accelerators == IntPtr.Zero || !owner._form.IsHandleCreated) {
                return false;
            }

            var form = owner._form.Handle;
            if (m.HWnd != form && !Win32Interop.IsChild(form, m.HWnd)) {
                return false;
            }

            var native = new Win32Interop.MSG {
                HWnd = m.HWnd,
                Message = (uint)m.Msg,
                WParam = m.WParam,
                LParam = m.LParam,
            };

            return Win32Interop.TranslateAcceleratorW(owner._form.Handle, owner._accelerators, ref native) != 0;
        }
    }
}
