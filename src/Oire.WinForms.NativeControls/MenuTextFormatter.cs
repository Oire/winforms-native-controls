namespace Oire.WinForms.NativeControls;

/// <summary>
/// Turns spec text into the strings Win32 menus expect, and reads mnemonics back out.
/// </summary>
/// <remarks>
/// Win32 menu item strings use <c>&amp;</c> to mark the mnemonic letter and a literal tab
/// to separate the label from its right-aligned accelerator text. <c>&amp;&amp;</c> escapes
/// a literal ampersand.
/// </remarks>
public static class MenuTextFormatter {
    /// <summary>
    /// Joins label and accelerator text with the tab Win32 aligns on. A null or blank
    /// <paramref name="shortcut"/> returns <paramref name="text"/> unchanged.
    /// </summary>
    public static string FormatForWin32(string text, string? shortcut) {
        ArgumentNullException.ThrowIfNull(text);
        return string.IsNullOrEmpty(shortcut) ? text : $"{text}\t{shortcut}";
    }

    /// <summary>
    /// Removes the mnemonic marker, collapsing <c>&amp;&amp;</c> to a single literal
    /// <c>&amp;</c>. <c>"&amp;New Note"</c> becomes <c>"New Note"</c>.
    /// </summary>
    public static string StripMnemonic(string text) {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains('&', StringComparison.Ordinal)) {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++) {
            if (text[i] != '&') {
                result.Append(text[i]);
                continue;
            }

            // "&&" is an escaped literal ampersand: emit one and skip both.
            if (i + 1 < text.Length && text[i + 1] == '&') {
                result.Append('&');
                i++;
            }
            // A lone '&' is the mnemonic marker — dropped.
        }

        return result.ToString();
    }

    /// <summary>
    /// Returns the uppercased character following the first unescaped <c>&amp;</c>, or null
    /// when the text carries no mnemonic. Uppercasing makes collision checks case-insensitive.
    /// </summary>
    public static char? ExtractMnemonic(string text) {
        ArgumentNullException.ThrowIfNull(text);
        for (var i = 0; i < text.Length; i++) {
            if (text[i] != '&') {
                continue;
            }

            if (i + 1 >= text.Length) {
                // Trailing '&' marks nothing.
                return null;
            }

            if (text[i + 1] == '&') {
                // Escaped literal — skip both characters and keep looking.
                i++;
                continue;
            }

            return char.ToUpperInvariant(text[i + 1]);
        }

        return null;
    }
}
