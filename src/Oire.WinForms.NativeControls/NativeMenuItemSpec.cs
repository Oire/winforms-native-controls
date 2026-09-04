namespace Oire.WinForms.NativeControls;

/// <summary>
/// One entry in a native menu: a plain command, a checkable toggle, a radio-group member,
/// a separator, or a submenu container.
/// </summary>
/// <remarks>
/// Deliberately a mutable class rather than a record. <see cref="IsEnabled"/> and
/// <see cref="IsChecked"/> are pushed into the live <c>HMENU</c> on every
/// <c>WM_INITMENUPOPUP</c>, so the owner mutates the spec in place and the menu follows.
/// A record would force a rebuild for every state flip.
/// </remarks>
public sealed class NativeMenuItemSpec {
    /// <summary>Display text, optionally carrying an <c>&amp;</c> mnemonic marker.</summary>
    public required string Text { get; init; }

    /// <summary>Accelerator text shown right-aligned in the menu (e.g. <c>"Ctrl+N"</c>), or null.</summary>
    public string? Shortcut { get; init; }

    /// <summary>
    /// Key chord to register in the form's accelerator table, or null when the shortcut is
    /// display-only (the owning control handles the chord itself, as with Ctrl+C in a text box).
    /// </summary>
    public Keys? ShortcutKeys { get; init; }

    /// <summary>Invoked when the item is chosen. Null for separators and submenu containers.</summary>
    public Action? OnClick { get; init; }

    /// <summary>Child items when this entry is a submenu, otherwise null.</summary>
    public IReadOnlyList<NativeMenuItemSpec>? Children { get; init; }

    /// <summary>True for a horizontal rule. Separators carry no text, id, or callback.</summary>
    public bool IsSeparator { get; init; }

    /// <summary>
    /// True when the item renders a check/radio mark and <see cref="IsChecked"/> is meaningful.
    /// Always true for radio-group members.
    /// </summary>
    public bool IsCheckable { get; init; }

    /// <summary>Grayed when false. Mutable — read afresh on every popup.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Check state, meaningful only when <see cref="IsCheckable"/> is true. Mutable — read
    /// afresh on every popup.
    /// </summary>
    public bool IsChecked { get; set; }

    /// <summary>
    /// Non-null marks the item as a radio-group member. Items sharing a group string are
    /// mutually exclusive and must be direct siblings under the same parent.
    /// </summary>
    public string? RadioGroup { get; init; }

    /// <summary>True when this entry opens a submenu.</summary>
    public bool IsSubMenu => Children is not null;
}
