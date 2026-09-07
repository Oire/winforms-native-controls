using Oire.WinForms.NativeControls;

namespace NoteBook;

/// <summary>
/// A deliberately small notes window, built to be listened to rather than looked at.
/// </summary>
/// <remarks>
/// <para>
/// The point of the layout is the Tab order. Focus moves category tree, then note list, then
/// the note text box, then back. The list is the interesting stop: it is a real
/// <c>SysListView32</c> hosted inside a WinForms container, so Tab has to leave a window that
/// WinForms does not own and land on a control that it does. That path is the one most likely
/// to break, and the one a listening test should walk first.
/// </para>
/// <para>
/// The second thing it exists for is the language switch. Mirroring the layout with English
/// text proves very little; Hebrew puts right-to-left text in the menus, the column headers and
/// the cells, and puts Hebrew mnemonics through the collision validator. Switching language
/// also drives <see cref="NativeMenuBar.Attach"/> again, which is the rebuild path a real
/// application takes when its catalog changes.
/// </para>
/// </remarks>
internal sealed class MainForm: Form {
    private readonly TreeView _categories = new();
    private readonly NativeListView _notes = new();
    private readonly TextBox _editor = new();
    private readonly Label _categoriesLabel = new();
    private readonly Label _notesLabel = new();
    private readonly Label _editorLabel = new();

    private NativeMenuBar? _menuBar;
    private NativeContextMenu? _noteMenu;
    private NativeContextMenu? _columnMenu;

    private bool _showModified = true;

    public MainForm() {
        // The manifest declares PerMonitorV2, so the form has to scale with the font or the
        // text grows on a high-DPI display while the layout around it does not.
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(880, 520);

        // Three percentage columns with no floor can be dragged down to slivers, taking the
        // tree and the list with them.
        MinimumSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        Strings.Changed += OnLanguageChanged;
        ApplyLanguage();
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);

        // The menu bar needs the form's HWND, so it is attached here rather than in the
        // constructor. SetMenu has nothing to attach to before this point.
        _menuBar = new NativeMenuBar(this);
        _menuBar.Attach(BuildMenuSpec());

        _noteMenu = new NativeContextMenu(BuildNoteMenuSpec());
        _columnMenu = new NativeContextMenu(BuildColumnMenuSpec());
        _noteMenu.Resolver = ResolveListMenu;
        _noteMenu.AttachTo(_notes);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) {
        if (disposing) {
            Strings.Changed -= OnLanguageChanged;

            // Before base.Dispose, while the HWND that owns the menu still exists.
            _menuBar?.Dispose();
            _noteMenu?.Dispose();
            _columnMenu?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout() {
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(8),
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Every control in the panel gets an explicit TabIndex, labels included. Controls.Add
        // assigns them in insertion order otherwise, and since the three labels are added
        // before the three controls they would take 0, 1, 2 and interleave with the controls'
        // own indices - which makes each label name the wrong control, and points each label's
        // mnemonic at the wrong one too. Measured, not assumed: without this the tree
        // announces as "Notes".
        //
        // A label names the next selectable control after it in tab order, which is the
        // mechanism this relies on. AccessibleName is set below as well, so nothing here
        // depends on that heuristic alone.
        _categoriesLabel.AutoSize = true;
        _categoriesLabel.TabIndex = 0;
        _notesLabel.AutoSize = true;
        _notesLabel.TabIndex = 2;
        _editorLabel.AutoSize = true;
        _editorLabel.TabIndex = 4;

        _categories.Dock = DockStyle.Fill;
        _categories.TabIndex = 1;
        _categories.HideSelection = false;
        _categories.AfterSelect += (_, _) => PopulateNotes();

        _notes.Dock = DockStyle.Fill;
        _notes.TabIndex = 3;
        _notes.MultiSelect = true;
        _notes.Columns.Add(new NativeListViewColumn("", NativeListViewColumn.AutoSizeToContent));
        _notes.Columns.Add(new NativeListViewColumn("", 70, NativeColumnAlignment.Right));
        _notes.Columns.Add(new NativeListViewColumn("", 140));
        _notes.SelectedIndexChanged += (_, _) => ShowSelectedNote();
        _notes.ItemActivate += (_, _) => {
            _editor.Focus();
            _editor.SelectAll();
        };
        _notes.ColumnClick += (_, e) => ToggleSort(e.Column);

        _editor.Dock = DockStyle.Fill;
        _editor.TabIndex = 5;
        _editor.Multiline = true;
        _editor.ScrollBars = ScrollBars.Vertical;

        layout.Controls.Add(_categoriesLabel, 0, 0);
        layout.Controls.Add(_notesLabel, 1, 0);
        layout.Controls.Add(_editorLabel, 2, 0);
        layout.Controls.Add(_categories, 0, 1);
        layout.Controls.Add(_notes, 1, 1);
        layout.Controls.Add(_editor, 2, 1);

        Controls.Add(layout);
    }

    /// <summary>
    /// Re-reads every user-visible string, and follows the language's reading direction.
    /// </summary>
    /// <remarks>
    /// A real application does exactly this on a catalog change. Note what has to be touched:
    /// the menu bar is rebuilt from a fresh spec, the column headers are reassigned, the rows
    /// are repopulated, and the list's accessible name is pushed again. Context menus need
    /// nothing, because they are already rebuilt on every invocation.
    /// </remarks>
    private void ApplyLanguage() {
        Text = Strings.Get("app.title");

        // Direction first: the list recreates its window when this changes, and the rows are
        // repopulated below anyway.
        var direction = Strings.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeft = direction;
        RightToLeftLayout = Strings.IsRightToLeft;

        _categoriesLabel.Text = Strings.Get("label.categories");
        _notesLabel.Text = Strings.Get("label.notes");
        _editorLabel.Text = Strings.Get("label.editor");

        // Named directly rather than through the label, so a change to the layout cannot
        // quietly rename a control.
        _categories.AccessibleName = Strings.Get("label.categories.plain");
        _notes.AccessibleName = Strings.Get("label.notes.plain");
        _editor.AccessibleName = Strings.Get("label.editor.plain");

        _notes.Columns[0].Text = Strings.Get("column.title");
        _notes.Columns[1].Text = Strings.Get("column.words");
        _notes.Columns[2].Text = Strings.Get("column.modified");

        PopulateCategories();
        PopulateNotes();

        // The menu bar is rebuilt rather than relabeled: the per-item right-to-left flags are
        // baked in at build time, so a direction change needs a fresh tree as much as new text
        // does. Attach on the same HWND rebuilds in place.
        _menuBar?.Attach(BuildMenuSpec());
    }

    private void OnLanguageChanged() => ApplyLanguage();

    private void PopulateCategories() {
        var selectedIndex = _categories.SelectedNode?.Index ?? -1;
        var wasRoot = _categories.SelectedNode?.Parent is null;

        _categories.BeginUpdate();
        try {
            _categories.Nodes.Clear();
            var all = _categories.Nodes.Add(Strings.Get("category.all"));
            all.Nodes.Add(Strings.Get("category.shopping"));
            all.Nodes.Add(Strings.Get("category.work"));
            all.Nodes.Add(Strings.Get("category.recipes"));
            all.Expand();

            _categories.SelectedNode = wasRoot || selectedIndex < 0
                ? all
                : all.Nodes[Math.Min(selectedIndex, all.Nodes.Count - 1)];
        } finally {
            _categories.EndUpdate();
        }
    }

    private void PopulateNotes() {
        _notes.BeginUpdate();
        try {
            _notes.Items.Clear();
            foreach (var note in SampleNotes()) {
                _notes.Items.Add(note);
            }
        } finally {
            _notes.EndUpdate();
        }

        if (_notes.Items.Count > 0) {
            // A list-like control with nothing selected announces nothing when focus reaches it,
            // so the first row is selected and focused up front.
            _notes.Items[0].Selected = true;
            _notes.Items[0].Focused = true;
        }

        ShowSelectedNote();
    }

    private IEnumerable<NativeListViewItem> SampleNotes() {
        var node = _categories.SelectedNode;
        var index = node?.Parent is null ? -1 : node.Index;

        var keys = index switch {
            0 => new[] { "shop", "hardware" },
            1 => new[] { "standup" },
            2 => new[] { "bread" },
            _ => new[] { "shop", "standup", "bread", "hardware" },
        };

        var words = new Dictionary<string, int>(StringComparer.Ordinal) {
            ["shop"] = 42,
            ["standup"] = 88,
            ["bread"] = 210,
            ["hardware"] = 12,
        };

        var modified = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["shop"] = "2026-09-01 09:15",
            ["standup"] = "2026-09-04 08:02",
            ["bread"] = "2026-08-30 14:22",
            ["hardware"] = "2026-08-27 17:40",
        };

        foreach (var key in keys) {
            yield return new NativeListViewItem(
                Strings.Get($"note.{key}.title"),
                words[key].ToString(),
                _showModified ? modified[key] : string.Empty) {
                Tag = Strings.Get($"note.{key}.body"),
            };
        }
    }

    private void ShowSelectedNote() =>
        _editor.Text = _notes.SelectedItems.Count > 0
            ? _notes.SelectedItems[0].Tag as string ?? string.Empty
            : string.Empty;

    private void ToggleSort(NativeListViewColumn column) {
        foreach (var other in _notes.Columns) {
            if (!ReferenceEquals(other, column)) {
                other.SortOrder = NativeSortOrder.None;
            }
        }

        // The control does not sort; the arrow is an indicator the application drives.
        column.SortOrder = column.SortOrder == NativeSortOrder.Ascending
            ? NativeSortOrder.Descending
            : NativeSortOrder.Ascending;
    }

    private NativeMenuSpec BuildMenuSpec() =>
        new NativeMenuSpec()
            .AddMenu(Strings.Get("menu.file"), file => file
                .Add(Strings.Get("menu.file.new"), Strings.Get("shortcut.new"),
                    Keys.Control | Keys.N, () => _editor.Focus())
                .AddSeparator()
                .Add(Strings.Get("menu.file.exit"), Strings.Get("shortcut.exit"),
                    shortcutKeys: null, Close))
            .AddMenu(Strings.Get("menu.view"), view => view
                .AddCheckable(Strings.Get("menu.view.modified"), _showModified, ToggleModifiedColumn)
                .AddSeparator()
                .AddMenu(Strings.Get("menu.view.language"), language => language
                    .AddRadio(Strings.Get("menu.view.language.english"), "language",
                        Strings.Current == Language.English, () => Strings.Use(Language.English))
                    .AddRadio(Strings.Get("menu.view.language.hebrew"), "language",
                        Strings.Current == Language.Hebrew, () => Strings.Use(Language.Hebrew))))
            .AddMenu(Strings.Get("menu.help"), help => help
                .Add(Strings.Get("menu.help.about"), ShowAbout));

    private NativeMenuSpec BuildNoteMenuSpec() {
        var hasSelection = _notes.SelectedItems.Count > 0;

        var spec = new NativeMenuSpec();
        spec.Add(Strings.Get("menu.note.open"), Strings.Get("shortcut.open"),
            shortcutKeys: null, () => _editor.Focus());
        spec.Items[^1].IsEnabled = hasSelection;
        spec.AddSeparator();
        spec.Add(Strings.Get("menu.note.selectall"), Strings.Get("shortcut.selectall"),
            shortcutKeys: null, SelectAllNotes);
        return spec;
    }

    private NativeMenuSpec BuildColumnMenuSpec() =>
        new NativeMenuSpec()
            .AddCheckable(Strings.Get("menu.view.modified"), _showModified, ToggleModifiedColumn);

    /// <summary>
    /// Picks the menu for a list invocation: the column menu when the pointer landed on the
    /// header, the note menu otherwise. A keyboard invocation always gets the note menu,
    /// because Shift+F10 carries no pointer position to hit-test.
    /// </summary>
    private NativeContextMenu? ResolveListMenu(NativeContextMenuRequest request) {
        if (!request.FromKeyboard && ListViewHeaderHitTest.IsOnHeader(_notes, request.ScreenLocation)) {
            _columnMenu?.Rebuild(BuildColumnMenuSpec());
            return _columnMenu;
        }

        _noteMenu?.Rebuild(BuildNoteMenuSpec());
        return _noteMenu;
    }

    private void SelectAllNotes() {
        foreach (var item in _notes.Items) {
            item.Selected = true;
        }
    }

    /// <summary>
    /// Flips the Modified column, and puts the new state back where the menu reads it from.
    /// </summary>
    /// <remarks>
    /// The library does not auto-toggle an independent checkable item - only radio siblings are
    /// updated for you - and it pushes each item's state from the spec on every
    /// <c>WM_INITMENUPOPUP</c>. So an application that flips its own flag without telling the
    /// spec gets a menu that reads the old value forever, which a screen reader then announces.
    /// Rebuilding the bar from a fresh spec is the simplest way to stay honest; the context
    /// menu needs nothing, because it is already rebuilt on every invocation.
    /// </remarks>
    private void ToggleModifiedColumn() {
        _showModified = !_showModified;
        PopulateNotes();
        _menuBar?.Rebuild(BuildMenuSpec());
    }

    private void ShowAbout() =>
        MessageBox.Show(
            this,
            Strings.Get("about.text"),
            Strings.Get("about.title"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1,
            // RtlReading alone reverses the reading order but leaves the text left-aligned;
            // the two flags are set together or the box looks half-converted.
            Strings.IsRightToLeft
                ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
                : 0);
}
