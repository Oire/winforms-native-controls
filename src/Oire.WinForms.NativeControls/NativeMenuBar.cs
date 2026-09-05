using System.Diagnostics.CodeAnalysis;

namespace Oire.WinForms.NativeControls;

/// <summary>
/// Owns a form's native menu bar: the <c>HMENU</c> tree, the accelerator table that fires its
/// shortcuts from anywhere in the form, and the window subclass that routes
/// <c>WM_COMMAND</c> and <c>WM_INITMENUPOPUP</c> back into the spec.
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
    private bool _disposed;

    /// <summary>Creates a menu bar owner for <paramref name="form"/>. Nothing is built until <see cref="Attach"/>.</summary>
    public NativeMenuBar(Form form) {
        ArgumentNullException.ThrowIfNull(form);
        _form = form;
    }

    /// <summary>
    /// Validates <paramref name="spec"/>, builds the menu, registers its accelerators, and
    /// puts it on the form. Calling this again on the same HWND is equivalent to
    /// <see cref="Rebuild"/>; calling it after a handle recreation rebinds to the new HWND.
    /// </summary>
    /// <exception cref="InvalidOperationException">
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

        var tree = NativeMenuTree.Build(spec, isMenuBar: true, rightToLeft: false);
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
    /// <exception cref="InvalidOperationException">
    /// A popup menu is currently being tracked, or the new spec fails validation.
    /// </exception>
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

        var newTree = NativeMenuTree.Build(spec, isMenuBar: true, rightToLeft: false);
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
    /// Subclasses the form's HWND to catch the two messages a native menu needs: the command
    /// the user chose, and the request to refresh a popup's state just before it is shown.
    /// </summary>
    private sealed class MenuMessageFilter(NativeMenuBar owner): NativeWindow {
        protected override void WndProc(ref Message m) {
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
    /// The <see cref="Form.ActiveForm"/> check keeps accelerators from firing while another
    /// form is active. That is correct as long as every secondary window is a
    /// <c>ShowDialog</c> modal owned by the menu's form, because <c>ActiveForm</c> then flips
    /// to the dialog for its lifetime and back afterwards. An application with modeless child
    /// windows needs an <c>Activated</c> / <c>Deactivate</c> subscription instead; that is a
    /// known limitation, tracked for 1.0.
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

            if (!ReferenceEquals(Form.ActiveForm, owner._form)) {
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
