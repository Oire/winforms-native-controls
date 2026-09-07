namespace NoteBook;

internal enum Language {
    English,
    Hebrew,
}

/// <summary>
/// The sample's string catalog, and the language it is currently showing.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a hand-written table rather than a real localization framework. The library
/// this sample demonstrates takes every user-visible string from its caller and imposes no
/// framework, so a sample that reached for gettext or resx would suggest an expectation the
/// library does not have. Map <see cref="Get"/> onto whatever catalog you already use; the
/// call site is the same shape either way.
/// </para>
/// <para>
/// Hebrew is here for a reason. Flipping <see cref="Control.RightToLeft"/> with English text
/// mirrors the layout but proves little; right-to-left text in the menus, the column headers
/// and the cells is what actually exercises the per-item <c>MFT_RIGHTORDER</c> flags, and
/// Hebrew mnemonics are what exercise the collision validator outside the Latin alphabet.
/// </para>
/// </remarks>
internal static class Strings {
    /// <summary>Raised after <see cref="Use"/> changes the language.</summary>
    internal static event Action? Changed;

    internal static Language Current { get; private set; } = Language.English;

    /// <summary>Whether the current language reads right to left.</summary>
    internal static bool IsRightToLeft => Current == Language.Hebrew;

    internal static void Use(Language language) {
        if (Current == language) {
            return;
        }

        Current = language;
        Changed?.Invoke();
    }

    /// <summary>
    /// The string for <paramref name="key"/> in the current language, falling back to the key
    /// itself so a missing entry is visible rather than blank.
    /// </summary>
    internal static string Get(string key) {
        // Accelerator names stay Latin in every language, so they live outside the catalogs
        // rather than being maintained identically in each one.
        if (Shortcuts.TryGetValue(key, out var shortcut)) {
            return shortcut;
        }

        var table = Current == Language.Hebrew ? Hebrew : English;
        return table.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>Accelerator display text, which does not vary by language.</summary>
    private static readonly Dictionary<string, string> Shortcuts = new(StringComparer.Ordinal) {
        ["shortcut.new"] = "Ctrl+N",
        ["shortcut.exit"] = "Alt+F4",
        ["shortcut.open"] = "Enter",
        ["shortcut.selectall"] = "Ctrl+A",
    };

    /// <summary>
    /// English. Mnemonics are the letter after each <c>&amp;</c>, and must be unique within one
    /// menu level or the library refuses to build the menu.
    /// </summary>
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal) {
        ["app.title"] = "NoteBook",

        ["menu.file"] = "&File",
        ["menu.file.new"] = "&New Note",
        ["menu.file.exit"] = "E&xit",
        ["menu.view"] = "&View",
        ["menu.view.modified"] = "Show &Modified column",
        ["menu.view.language"] = "&Language",
        ["menu.view.language.english"] = "&English",
        ["menu.view.language.hebrew"] = "&Hebrew",
        ["menu.help"] = "&Help",
        ["menu.help.about"] = "&About",

        ["menu.note.open"] = "&Open",
        ["menu.note.selectall"] = "Select &All",

        ["label.categories"] = "&Categories",
        ["label.notes"] = "N&otes",
        ["label.categories.plain"] = "Categories",
        ["label.notes.plain"] = "Notes",
        ["label.editor.plain"] = "Note text",
        ["label.editor"] = "&Note text",

        ["column.title"] = "Title",
        ["column.words"] = "Words",
        ["column.modified"] = "Modified",

        ["category.all"] = "All notes",
        ["category.shopping"] = "Shopping",
        ["category.work"] = "Work",
        ["category.recipes"] = "Recipes",

        ["note.shop.title"] = "Weekly shop",
        ["note.shop.body"] = "Oats, coffee, olive oil, tinned tomatoes.",
        ["note.standup.title"] = "Standup notes",
        ["note.standup.body"] = "Blocked on the release pipeline.",
        ["note.bread.title"] = "Bread",
        ["note.bread.body"] = "Overnight sponge, bake at 240C.",
        ["note.hardware.title"] = "Hardware",
        ["note.hardware.body"] = "Picture hooks and a small screwdriver.",

        ["about.text"] = "A sample for Oire.WinForms.NativeControls.\n\n"
            + "Tab between the tree, the list and the text box, and listen.",
        ["about.title"] = "About NoteBook",

    };

    /// <summary>
    /// Hebrew. The mnemonic letters were chosen to be distinct within each menu level, which is
    /// the same discipline a translator has to keep and the thing the validator checks.
    /// </summary>
    private static readonly Dictionary<string, string> Hebrew = new(StringComparer.Ordinal) {
        ["app.title"] = "פנקס",

        ["menu.file"] = "&קובץ",
        ["menu.file.new"] = "פתק &חדש",
        ["menu.file.exit"] = "&יציאה",
        ["menu.view"] = "&תצוגה",
        ["menu.view.modified"] = "&הצג עמודת שינוי",
        ["menu.view.language"] = "&שפה",
        ["menu.view.language.english"] = "&אנגלית",
        ["menu.view.language.hebrew"] = "&עברית",
        ["menu.help"] = "ע&זרה",
        ["menu.help.about"] = "&אודות",

        ["menu.note.open"] = "&פתח",
        ["menu.note.selectall"] = "בחר ה&כל",

        ["label.categories"] = "&קטגוריות",
        ["label.notes"] = "&פתקים",
        ["label.categories.plain"] = "קטגוריות",
        ["label.notes.plain"] = "פתקים",
        ["label.editor.plain"] = "טקסט הפתק",
        ["label.editor"] = "&טקסט הפתק",

        ["column.title"] = "כותרת",
        ["column.words"] = "מילים",
        ["column.modified"] = "שונה",

        ["category.all"] = "כל הפתקים",
        ["category.shopping"] = "קניות",
        ["category.work"] = "עבודה",
        ["category.recipes"] = "מתכונים",

        ["note.shop.title"] = "קניות שבועיות",
        ["note.shop.body"] = "שיבולת שועל, קפה, שמן זית, עגבניות משומרות.",
        ["note.standup.title"] = "הערות ישיבה",
        ["note.standup.body"] = "תקוע בגלל צינור ההפצה.",
        ["note.bread.title"] = "לחם",
        ["note.bread.body"] = "בצק לילה, לאפות ב-240 מעלות.",
        ["note.hardware.title"] = "כלי עבודה",
        ["note.hardware.body"] = "ווים לתמונות ומברג קטן.",

        // U+2068/U+2069 isolate the Latin run so the sentence's punctuation stays put.
        ["about.text"] = "דוגמה עבור \u2068Oire.WinForms.NativeControls\u2069.\n\n"
            + "עברו עם Tab בין העץ, הרשימה ותיבת הטקסט, והקשיבו.",
        ["about.title"] = "אודות פנקס",

    };
}
