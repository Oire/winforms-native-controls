# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). From 1.0
onward the public API is stable: additions come in a minor release and breaking changes only
in a major one.

## [Unreleased]

## [1.0.0] - 2026-09-06

First stable release. The public API is settled; see the versioning note above.

### Fixed

- Menu accelerators now decide whether a keystroke is theirs by asking whether the message is
  aimed at the owning form or anything inside it, rather than by consulting `Form.ActiveForm`.
  That property answers a different question and got this one wrong in two shipping cases: for
  an MDI child it names the MDI *parent*, so a child's own menu bar never fired; and it is null
  whenever the active window is not a WinForms `Form`, which is the normal state of a mixed WPF
  or native host. Modal dialogs are still excluded, because a dialog is owned by the form but is
  not a child of it. This removes the last of the limitations previously tracked for 1.0.
- A keyboard-invoked `NativeContextMenu` now anchors at the focused row of a `NativeListView`,
  as it already did for a WinForms `ListView`, `TreeView` and `ListBox`. `NativeListView` is a
  `Control` rather than a `ListView`, so it had been falling through to the control's top-left
  corner - the library's own list control was the one case its context menus did not know about.
- Clearing `NativeListView.AccessibleName` now reaches the list window. The name was only ever
  pushed when non-empty, so a reader went on announcing a name the application had taken away.
- `NativeMenuSpec` rejects a null item label at the builder call. It previously survived until
  validation walked the tree for mnemonics, and the exception then named a parameter of a
  formatter the caller never called.

### Added

- `NativeListView.BackColor` and `ForeColor` are honored. The list window paints itself, so both
  are pushed across with `LVM_SETBKCOLOR`, `LVM_SETTEXTBKCOLOR` and `LVM_SETTEXTCOLOR`; they
  previously looked settable and did nothing. Both default to the window colors rather than
  inheriting the parent's dialog gray, matching what a WinForms `ListView` does.
- `NativeListView` follows the system theme, which is a consequence of the above rather than a
  separate feature. Left alone the colors resolve through `SystemColors`, which WinForms remaps
  for the application's color mode, so the list now goes dark with the rest of a dark-mode
  application and honors a high-contrast theme. Previously nothing was pushed at all and the
  control fell back on the raw `GetSysColor(COLOR_WINDOW)`, which stays white however the
  application is themed - a white list in a dark window. The visual style is chosen to match
  (`DarkMode_Explorer` rather than `Explorer`), without which the header, the scroll bars and
  the hover highlight stay light on an otherwise dark list, and both are re-applied on
  `WM_SYSCOLORCHANGE` so a theme switched while the application is running is followed.
- `NativeListViewColumn.Alignment` is settable rather than constructor-only, which is what
  every other column property already was. Assigning it preserves the sort arrow, which shares
  the same Win32 format word and a plain write would have cleared. Column zero is still drawn
  left-aligned whatever it is set to - that is a rule of the report-mode control itself.
- Package icon and package title.

### Changed


- **Breaking.** `NativeListView` no longer derives from `ListView`. It is a `Control` that
  creates a genuine `SysListView32` child window and drives it with `LVM_*` messages, with
  its own `NativeListViewItem` and `NativeListViewColumn` model.

  The previous approach - subclassing `ListView` and declining WinForms' UI Automation
  provider - was measured by ear across JAWS, NVDA and Narrator and bought nothing. JAWS
  reads every column from the stock control too, and NVDA reads only the first from all of
  them, gaining a spurious "not selected" on every arrow. The cause is the window class:
  WinForms registers `WindowsForms10.SysListView32.app.0…`, and both UI Automation and NVDA
  select their list handling by class name. Declining the provider leaves a bare `Pane` with
  no items; keeping it leaves a `Table` whose `GridPattern.GetItem` returns nothing usable.
  No subclass can change a window's class, so the control now creates the real one - the
  same class wxWidgets creates, which reads every column on every reader.
- `NativeListView` registers the list window as an OLE drop target and forwards to the
  ordinary `DragEnter` / `DragOver` / `DragLeave` / `DragDrop` events, so drag and drop is
  written the same way as for any other control. WinForms registers only the container, which
  the list window covers, and OLE resolves a drop against the window under the cursor.
- `NativeListView` supports per-row `ForeColor` through custom draw, auto-sizing column
  widths (`AutoSizeToContent` / `AutoSizeToHeader`), and `GetItemBounds` for positioning a
  drop indicator against a row.
- `NativeListView` carries a `BorderStyle`, defaulting to `Fixed3D` as a WinForms `ListView`
  and `TreeView` both do; without one the list sat flush against its panel while its
  neighbors were inset, and its header stopped reading as a header.
- `NativeListView` opts into the Explorer visual style, which is what gives a list its hover
  highlight, current selection styling and modern row metrics.
- `NativeListView` reports a preferred size derived from its font. A control that reports
  none is not merely unopinionated: a `TableLayoutPanel` divides a row-spanning neighbor's
  height by what each row asks for, so zero hands the whole share to an auto-sized row,
  which grows and pushes the list down the panel.


## [0.1.1] - 2026-09-05

### Fixed

- Closing a menu no longer leaves a screen reader announcing the menu. Native menu mode never
  moves the keyboard focus, so leaving it fires no focus event, and a reader has to ask what
  holds focus rather than being told - an answer that can still name the menu that just
  closed. It shows up most clearly with `NativeListView`, which routes accessibility through
  MSAA rather than UI Automation. `NativeMenuBar` and `NativeContextMenu` now raise
  `EVENT_OBJECT_FOCUS` for the focused window when a menu is dismissed, and stay silent when
  an item was chosen, so a command's own announcement is not spoken over.
- `NativeMenuBar` now follows its form's `RightToLeft`. It always built the menu bar left to
  right, so on a right-to-left layout the dropdown items never got
  `MFT_RIGHTORDER | MFT_RIGHTJUSTIFY`: submenus opened on the wrong side and the arrow keys
  that open them stayed reversed. `WS_EX_LAYOUTRTL` mirrors the bar itself but not popups,
  which are separate windows. `NativeContextMenu` already read its control's direction.

## [0.1.0] - 2026-09-05

First release.

### Added

- `NativeMenuSpec` / `NativeMenuItemSpec` - declarative menu description covering items,
  submenus, separators, checkable items and radio groups.
- `NativeMenuBar` - owns the `HMENU` tree, a Win32 accelerator table and the form subclass that
  routes `WM_COMMAND` and `WM_INITMENUPOPUP` back into the spec.
- `NativeContextMenu` - popup menus bound to a control, driven by `WM_CONTEXTMENU` so
  right-click, Shift+F10, the Applications key and touch long-press all take one code path.
  A `Resolver` hook lets one control carry more than one menu and rebuild per invocation.
- `MenuSpecValidator` - rejects duplicate mnemonics within a menu level and malformed radio
  groups, before any handle is allocated.
- `MenuTextFormatter` - mnemonic parsing and Win32 accelerator-text formatting.
- `AccelConverter` - `Keys` to `ACCEL` mapping.
- `ListViewHeaderHitTest` - whether a screen point falls on a `ListView`'s column-header band.
- Right-to-left popup support via `MFT_RIGHTORDER | MFT_RIGHTJUSTIFY`, which
  `WS_EX_LAYOUTRTL` alone does not provide.
- `NativeListView` - a `ListView` that declines WinForms' UI Automation provider, so screen
  readers read every column again instead of only the first.
  WinForms reports a Details-mode list as `ControlType.Table` but its
  `GridPattern.GetItem(row, column)` returns unusable elements, which breaks cell navigation on
  JAWS, NVDA and Narrator alike.

[Unreleased]: https://github.com/Oire/winforms-native-controls/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Oire/winforms-native-controls/releases/tag/v1.0.0
[0.1.1]: https://github.com/Oire/winforms-native-controls/releases/tag/v0.1.1
[0.1.0]: https://github.com/Oire/winforms-native-controls/releases/tag/v0.1.0
