namespace Oire.WinForms.NativeControls;

/// <summary>
/// Declarative description of a menu, built fluently and handed to
/// <see cref="NativeMenuBar"/> or <see cref="NativeContextMenu"/> to be turned into a
/// Win32 <c>HMENU</c>. Pure data — no interop happens here.
/// </summary>
/// <remarks>
/// The same type describes a menu bar and a popup menu, because Win32 makes no structural
/// distinction: a menu bar is an <c>HMENU</c> whose items each carry a child <c>HMENU</c>,
/// and a popup is an <c>HMENU</c> of leaf items. <see cref="AddMenu"/> therefore covers both
/// "top-level bar entry" and "nested submenu".
/// </remarks>
public sealed class NativeMenuSpec {
    private readonly List<NativeMenuItemSpec> _items = [];

    /// <summary>Items in declaration order.</summary>
    public IReadOnlyList<NativeMenuItemSpec> Items => _items;

    /// <summary>Adds a plain command item with no accelerator.</summary>
    public NativeMenuSpec Add(string text, Action onClick) =>
        Add(text, shortcut: null, shortcutKeys: null, onClick);

    /// <summary>
    /// Adds a plain command item. Pass <paramref name="shortcutKeys"/> as null to show the
    /// accelerator text without registering the chord in the accelerator table.
    /// </summary>
    public NativeMenuSpec Add(string text, string? shortcut, Keys? shortcutKeys, Action onClick) {
        ArgumentNullException.ThrowIfNull(onClick);
        _items.Add(new NativeMenuItemSpec {
            Text = text,
            Shortcut = shortcut,
            ShortcutKeys = shortcutKeys,
            OnClick = onClick,
        });
        return this;
    }

    /// <summary>
    /// Adds a submenu. At the top level of a menu-bar spec this becomes a bar entry
    /// (File, Edit, ...); nested inside another <see cref="AddMenu"/> it becomes a submenu.
    /// </summary>
    public NativeMenuSpec AddMenu(string text, Action<NativeMenuSpec> build) {
        ArgumentNullException.ThrowIfNull(build);
        var child = new NativeMenuSpec();
        build(child);
        _items.Add(new NativeMenuItemSpec {
            Text = text,
            Children = child.Items,
        });
        return this;
    }

    /// <summary>Adds a horizontal separator.</summary>
    public NativeMenuSpec AddSeparator() {
        _items.Add(new NativeMenuItemSpec { Text = string.Empty, IsSeparator = true });
        return this;
    }

    /// <summary>Adds an independent on/off toggle, drawn with a checkmark when checked.</summary>
    public NativeMenuSpec AddCheckable(string text, bool isChecked, Action onClick) =>
        AddCheckable(text, isChecked, shortcut: null, shortcutKeys: null, onClick);

    /// <summary>Adds an independent on/off toggle, drawn with a checkmark when checked.</summary>
    public NativeMenuSpec AddCheckable(string text, bool isChecked, string? shortcut, Keys? shortcutKeys, Action onClick) {
        ArgumentNullException.ThrowIfNull(onClick);
        _items.Add(new NativeMenuItemSpec {
            Text = text,
            Shortcut = shortcut,
            ShortcutKeys = shortcutKeys,
            OnClick = onClick,
            IsCheckable = true,
            IsChecked = isChecked,
        });
        return this;
    }

    /// <summary>
    /// Adds a member of a mutually exclusive group, drawn with a radio bullet when checked.
    /// All items sharing <paramref name="radioGroup"/> must be siblings in this same menu.
    /// </summary>
    public NativeMenuSpec AddRadio(string text, string radioGroup, bool isChecked, Action onClick) =>
        AddRadio(text, radioGroup, isChecked, shortcut: null, shortcutKeys: null, onClick);

    /// <summary>
    /// Adds a member of a mutually exclusive group, drawn with a radio bullet when checked.
    /// All items sharing <paramref name="radioGroup"/> must be siblings in this same menu.
    /// </summary>
    public NativeMenuSpec AddRadio(string text, string radioGroup, bool isChecked, string? shortcut, Keys? shortcutKeys, Action onClick) {
        ArgumentException.ThrowIfNullOrWhiteSpace(radioGroup);
        ArgumentNullException.ThrowIfNull(onClick);
        _items.Add(new NativeMenuItemSpec {
            Text = text,
            Shortcut = shortcut,
            ShortcutKeys = shortcutKeys,
            OnClick = onClick,
            IsCheckable = true,
            IsChecked = isChecked,
            RadioGroup = radioGroup,
        });
        return this;
    }
}
