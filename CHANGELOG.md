# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the
version is `0.x` the public API may change in a minor release.

## [Unreleased]

## [0.1.0] - 2026-09-05

First release. Extracted from the Notika desktop application, where it replaced WinForms'
`MenuStrip` and `ContextMenuStrip` throughout.

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
- `NativeListView` and `ListAccessibilityMode` - a `ListView` that declines WinForms' UI
  Automation provider, so screen readers read every column again instead of only the first.
  WinForms reports a Details-mode list as `ControlType.Table` but its
  `GridPattern.GetItem(row, column)` returns unusable elements, which breaks cell navigation on
  JAWS, NVDA and Narrator alike.

[Unreleased]: https://github.com/Oire/winforms-native-controls/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Oire/winforms-native-controls/releases/tag/v0.1.0
