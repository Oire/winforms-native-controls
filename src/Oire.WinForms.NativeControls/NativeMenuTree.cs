using System.Diagnostics.CodeAnalysis;

namespace Oire.WinForms.NativeControls;

/// <summary>
/// A live <c>HMENU</c> tree built from a <see cref="NativeMenuSpec"/>, plus the bookkeeping
/// needed to route a command id back to its item and to push item state into Windows.
/// </summary>
/// <remarks>
/// Shared by <see cref="NativeMenuBar"/> and <see cref="NativeContextMenu"/>: Win32 draws no
/// structural distinction between a menu bar and a popup, so neither does this. The only
/// difference is the root, created by <c>CreateMenu</c> for a bar and <c>CreatePopupMenu</c>
/// for a popup.
/// </remarks>
internal sealed class NativeMenuTree: IDisposable {
    /// <summary>First command id handed out. Low enough to stay clear of <c>SC_*</c> system commands.</summary>
    private const ushort FirstCommandId = 0x1000;

    /// <summary>
    /// Last command id handed out. <c>WM_COMMAND</c> only carries the low word of
    /// <c>wParam</c>, so ids must fit in a <see cref="ushort"/>; stopping at <c>0xDFFF</c>
    /// also keeps clear of the <c>0xE000</c>–<c>0xF000</c> system-command range.
    /// </summary>
    private const ushort LastCommandId = 0xDFFF;

    private readonly Dictionary<ushort, MenuCommand> _commands = [];
    private readonly Dictionary<IntPtr, List<MenuEntry>> _levels = [];
    private readonly bool _rightToLeft;
    private ushort _nextId = FirstCommandId;
    private bool _disposed;

    private NativeMenuTree(IntPtr handle, bool rightToLeft) {
        Handle = handle;
        _rightToLeft = rightToLeft;
    }

    /// <summary>Root <c>HMENU</c>.</summary>
    internal IntPtr Handle { get; }

    /// <summary>Command id to item, for every clickable leaf in the tree.</summary>
    internal IReadOnlyDictionary<ushort, MenuCommand> Commands => _commands;

    /// <summary>
    /// Builds the whole <c>HMENU</c> tree and pushes each item's initial enabled and checked
    /// state, so the menu is correct before the first <c>WM_INITMENUPOPUP</c> — which matters
    /// when an accelerator fires before the menu has ever been opened.
    /// </summary>
    internal static NativeMenuTree Build(NativeMenuSpec spec, bool isMenuBar, bool rightToLeft) {
        var root = isMenuBar ? Win32Interop.CreateMenu() : Win32Interop.CreatePopupMenu();
        if (root == IntPtr.Zero) {
            throw new InvalidOperationException("Windows refused to create the menu handle.");
        }

        var tree = new NativeMenuTree(root, rightToLeft);
        try {
            tree.Populate(root, spec.Items);
            tree.PushAllState();
        } catch {
            tree.Dispose();
            throw;
        }

        return tree;
    }

    /// <summary>Looks up a command id without invoking or mutating anything.</summary>
    internal bool TryGetCommand(ushort id, [NotNullWhen(true)] out MenuCommand? command) =>
        _commands.TryGetValue(id, out command);

    /// <summary>
    /// Resolves a chosen command id to the callback to run. Returns null when the id is
    /// unknown or the item is currently disabled — the disabled check is what stops an
    /// accelerator from firing an item the user could not have clicked.
    /// </summary>
    /// <remarks>
    /// For a radio item the sibling group is updated here, before the caller runs the
    /// callback, so the callback and the next <c>WM_INITMENUPOPUP</c> both see the new state.
    /// </remarks>
    internal Action? Resolve(ushort id) {
        if (!_commands.TryGetValue(id, out var command) || !command.Spec.IsEnabled) {
            return null;
        }

        if (command.Spec.RadioGroup is { } group) {
            foreach (var sibling in command.Siblings) {
                if (string.Equals(sibling.RadioGroup, group, StringComparison.Ordinal)) {
                    sibling.IsChecked = ReferenceEquals(sibling, command.Spec);
                }
            }
        }

        return command.Spec.OnClick;
    }

    /// <summary>
    /// Pushes current enabled and checked state for one popup level, in response to
    /// <c>WM_INITMENUPOPUP</c>. Unknown handles (a system menu, for instance) are ignored.
    /// </summary>
    internal void PushState(IntPtr popupHandle) {
        if (_levels.TryGetValue(popupHandle, out var entries)) {
            PushLevelState(popupHandle, entries);
        }
    }

    /// <summary>Pushes state for every level, used once at build time.</summary>
    internal void PushAllState() {
        foreach (var (handle, entries) in _levels) {
            PushLevelState(handle, entries);
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        if (Handle != IntPtr.Zero) {
            // DestroyMenu walks into every attached submenu, so the root call is enough.
            Win32Interop.DestroyMenu(Handle);
        }

        _commands.Clear();
        _levels.Clear();
    }

    private void Populate(IntPtr menu, IReadOnlyList<NativeMenuItemSpec> items) {
        var entries = new List<MenuEntry>(items.Count);
        _levels[menu] = entries;

        for (var i = 0; i < items.Count; i++) {
            var item = items[i];
            var position = (uint)i;

            if (item.IsSeparator) {
                Append(menu, Win32Interop.MF_SEPARATOR, UIntPtr.Zero, null);
                entries.Add(new MenuEntry(item, position, Id: 0));
                continue;
            }

            var label = MenuTextFormatter.FormatForWin32(item.Text, item.Shortcut);

            if (item.Children is { } children) {
                var submenu = Win32Interop.CreatePopupMenu();
                if (submenu == IntPtr.Zero) {
                    throw new InvalidOperationException($"Windows refused to create the submenu for '{item.Text}'.");
                }

                Populate(submenu, children);
                Append(menu, Win32Interop.MF_STRING | Win32Interop.MF_POPUP, unchecked((UIntPtr)(nuint)(nint)submenu), label);
                entries.Add(new MenuEntry(item, position, Id: 0));
            } else {
                var id = AllocateId();
                _commands[id] = new MenuCommand(item, items);
                Append(menu, Win32Interop.MF_STRING, (UIntPtr)id, label);
                entries.Add(new MenuEntry(item, position, id));
            }

            ApplyItemType(menu, position, item);
        }
    }

    private static void Append(IntPtr menu, uint flags, UIntPtr idOrSubmenu, string? label) {
        if (!Win32Interop.AppendMenuW(menu, flags, idOrSubmenu, label)) {
            throw new InvalidOperationException($"AppendMenu failed for '{label ?? "(separator)"}'.");
        }
    }

    /// <summary>
    /// Applies the item's <c>fType</c> bits: the radio-bullet glyph for radio-group members,
    /// and the right-to-left ordering flags for popups on RTL layouts. Skipped entirely when
    /// neither applies, so the common case costs nothing.
    /// </summary>
    private void ApplyItemType(IntPtr menu, uint position, NativeMenuItemSpec item) {
        var isRadio = item.RadioGroup is not null;
        if (!isRadio && !_rightToLeft) {
            return;
        }

        var info = Win32Interop.MENUITEMINFOW.Create();
        info.FMask = Win32Interop.MIIM_FTYPE;
        info.FType = ItemType(item);
        Win32Interop.SetMenuItemInfoW(menu, position, fByPosition: true, ref info);
    }

    private uint ItemType(NativeMenuItemSpec item) {
        var type = item.IsSeparator ? Win32Interop.MFT_SEPARATOR : Win32Interop.MFT_STRING;
        if (item.RadioGroup is not null) {
            type |= Win32Interop.MFT_RADIOCHECK;
        }

        if (_rightToLeft) {
            // WS_EX_LAYOUTRTL mirrors the menu bar but not popups; these flags do the popups.
            type |= Win32Interop.MFT_RIGHTORDER | Win32Interop.MFT_RIGHTJUSTIFY;
        }

        return type;
    }

    private void PushLevelState(IntPtr menu, List<MenuEntry> entries) {
        foreach (var entry in entries) {
            var item = entry.Spec;
            if (item.IsSeparator) {
                continue;
            }

            if (item.RadioGroup is not null) {
                // One call carries enabled state, checked state, and the radio glyph. Setting
                // MIIM_STATE replaces fState wholesale, so the enabled bits go in here too.
                var info = Win32Interop.MENUITEMINFOW.Create();
                info.FMask = Win32Interop.MIIM_STATE | Win32Interop.MIIM_FTYPE;
                info.FType = ItemType(item);
                info.FState =
                    (item.IsEnabled ? Win32Interop.MFS_ENABLED : Win32Interop.MFS_DISABLED) |
                    (item.IsChecked ? Win32Interop.MFS_CHECKED : Win32Interop.MFS_UNCHECKED);
                Win32Interop.SetMenuItemInfoW(menu, entry.Position, fByPosition: true, ref info);
                continue;
            }

            // Return values are the previous state, not a success code — nothing to check.
            _ = Win32Interop.EnableMenuItem(
                menu,
                entry.Position,
                Win32Interop.MF_BYPOSITION | (item.IsEnabled ? Win32Interop.MF_ENABLED : Win32Interop.MF_GRAYED));

            if (item.IsCheckable) {
                _ = Win32Interop.CheckMenuItem(
                    menu,
                    entry.Position,
                    Win32Interop.MF_BYPOSITION | (item.IsChecked ? Win32Interop.MF_CHECKED : Win32Interop.MF_UNCHECKED));
            }
        }
    }

    private ushort AllocateId() {
        if (_nextId > LastCommandId) {
            throw new InvalidOperationException(
                $"Ran out of menu command ids; the range {FirstCommandId:X4}-{LastCommandId:X4} is exhausted.");
        }

        return _nextId++;
    }
}

/// <summary>
/// A clickable item together with the sibling list it lives in. The siblings are what radio
/// mutual exclusion walks when the item is chosen.
/// </summary>
internal sealed record MenuCommand(NativeMenuItemSpec Spec, IReadOnlyList<NativeMenuItemSpec> Siblings);

/// <summary>
/// One entry in a built menu level. Addressed by position rather than command id so that
/// separators and submenu containers — neither of which has an id — are covered too.
/// </summary>
internal sealed record MenuEntry(NativeMenuItemSpec Spec, uint Position, ushort Id);
