# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the
version is `0.x` the public API may change in a minor release.

## [Unreleased]

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

[Unreleased]: https://github.com/Oire/winforms-native-controls/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/Oire/winforms-native-controls/releases/tag/v0.1.1
[0.1.0]: https://github.com/Oire/winforms-native-controls/releases/tag/v0.1.0
