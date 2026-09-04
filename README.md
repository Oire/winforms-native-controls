# Oire.WinForms.NativeControls

[![NuGet version](https://img.shields.io/nuget/v/Oire.WinForms.NativeControls?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Oire.WinForms.NativeControls)
[![NuGet downloads](https://img.shields.io/nuget/dt/Oire.WinForms.NativeControls?logo=nuget&label=downloads)](https://www.nuget.org/packages/Oire.WinForms.NativeControls)
[![Build status](https://github.com/Oire/winforms-native-controls/actions/workflows/dotnet.yml/badge.svg?branch=master)](https://github.com/Oire/winforms-native-controls/actions/workflows/dotnet.yml)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue)](LICENSE)

Win32-native replacements for the WinForms controls that screen readers handle poorly — starting with menus.

WinForms reimplemented its menus in managed code, and screen readers have paid for it ever
since. `MenuStrip` and `ContextMenuStrip` announce generically, render submenus poorly, and are
sluggish on first open no matter how much you pre-warm them. This puts menus back on real Win32
`HMENU` handles, where every screen reader has understood them for thirty years.

## Features

* **Real Win32 menus, not a repainted imitation.** A menu bar is a genuine `HMENU` attached with
  `SetMenu`; a context menu is a genuine popup shown with `TrackPopupMenuEx`. Screen readers
  announce them as a menu bar, a submenu and a popup menu, because that is exactly what they are.
* **One code path for every way a context menu can be invoked.** Right-click, Shift+F10, the
  Applications key and touch long-press all arrive as `WM_CONTEXTMENU`, so the keyboard route is
  not a bolted-on extra that quietly rots — it is the same code the mouse uses.
* **Declared, not assembled.** Menus are described as data and handed over in one call, so the
  structure of an application's menus can be read in one place instead of reconstructed from a
  hundred scattered `Items.Add` calls.
* **Mnemonic collisions stop the build, not the user.** Two items in the same menu sharing an
  access key is a real accessibility defect that normally survives for years. Here it throws
  before a single handle is allocated, naming both offenders.
* **Accelerators that fire from anywhere in the form.** Shortcuts go into a real Win32
  accelerator table, with a display-only mode for the chords a focused control should keep.
* **Localization-agnostic.** Every user-visible string comes from the caller, so applications
  localize menus with whatever framework they already use and this library imposes none.
* **Right-to-left aware.** Popups get the per-item `MFT_RIGHTORDER | MFT_RIGHTJUSTIFY` flags that
  `WS_EX_LAYOUTRTL` does not apply on its own.
* **A `ListView` that reads all of its columns again.** WinForms reports a Details-mode list to
  UI Automation as a table whose cell navigation does not work, so screen readers read only the
  first column and cannot move across. `NativeListView` gets that provider out of the way.
* **Packaged properly.** XML documentation for IntelliSense, deterministic builds, and
  SourceLink-enabled `snupkg` symbols so you can step straight into the library source.

## Installation

Install via the .NET CLI:

```
dotnet add package Oire.WinForms.NativeControls
```

Or via the Package Manager Console in Visual Studio:

```
Install-Package Oire.WinForms.NativeControls
```

Targets `net10.0-windows`, AnyCPU. No dependencies beyond the framework itself.

## Quick start

A menu bar is declared, not assembled. Build a spec, hand it to the bar:

```csharp
using Oire.WinForms.NativeControls;

var spec = new NativeMenuSpec()
    .AddMenu("&File", file => file
        .Add("&New Note", "Ctrl+N", Keys.Control | Keys.N, OnNewNote)
        .AddSeparator()
        .Add("E&xit", "Ctrl+Q", Keys.Control | Keys.Q, OnExit))
    .AddMenu("&View", view => view
        .AddCheckable("Show &Preview", isChecked: true, OnTogglePreview));

_menuBar = new NativeMenuBar(this);   // this == your Form
_menuBar.Attach(spec);
```

Attach it once the form has a handle — `OnHandleCreated` is the natural place — and dispose it
in `Dispose(bool)` **before** `base.Dispose`, while the HWND that owns the menu still exists.

A context menu binds to one control and handles right-click, Shift+F10, the Applications key
and touch long-press through the single `WM_CONTEXTMENU` path:

```csharp
_noteMenu = new NativeContextMenu(BuildNoteMenuSpec());
_noteMenu.AttachTo(noteListView);
```

Per-invocation state — enabled, checked, conditional items — is expressed by rebuilding the
spec in a `Resolver`, which also lets one control carry more than one menu:

```csharp
_noteMenu.Resolver = request => {
    if (!request.FromKeyboard && ListViewHeaderHitTest.IsOnHeader(noteListView, request.ScreenLocation)) {
        _columnMenu.Rebuild(BuildColumnMenuSpec());
        return _columnMenu;
    }

    _noteMenu.Rebuild(BuildNoteMenuSpec());
    return _noteMenu;
};
```

## What is it?

WinForms 1.0 shipped `MainMenu` and `ContextMenu`, thin wrappers over the Win32 menu API. .NET 2.0
replaced them with the `ToolStrip` family — `MenuStrip`, `ContextMenuStrip` — which are managed
controls that draw themselves. They look more modern, they support images and arbitrary hosted
controls, and they are far easier to restyle.

They are also, for a screen-reader user, a step backwards. A `MenuStrip` is not a menu as far as
the operating system is concerned; it is a panel drawing things that look like menu items, with an
accessibility tree assembled alongside. Announcements are generic, submenus render awkwardly, and
the first open is slow because that tree is built lazily, on demand, while the screen reader waits.

The obvious fix — go back to `MainMenu` — is not available. Those classes still exist in modern
.NET for binary compatibility, but every constructor throws `PlatformNotSupportedException` and
every method is a stub. There is no supported route back to native menus in WinForms.

This library is that route. It is deliberately narrow: it addresses the places where a WinForms
managed layer hurt accessibility, and nothing else. Where a WinForms control is still a thin
wrapper over a real Win32 control and behaves, there is nothing here to fix — the goal is to get
out of the way of three decades of screen-reader compatibility work, not to add another layer.

The same principle explains why `NativeListView` exists even though a WinForms `ListView` really
is a `SysListView32`. The control is fine; the UI Automation provider layered on top of it is
not. See its own section below.

It was extracted from Notika, a note-taking application whose primary maintainer is blind, after
the `MenuStrip` sluggishness proved unfixable by every pre-warming trick available.

## What's in the box

| Type | Purpose |
| --- | --- |
| `NativeMenuSpec` / `NativeMenuItemSpec` | Declarative menu description — items, submenus, separators, checkables, radio groups |
| `NativeMenuBar` | Owns the `HMENU`, the accelerator table and the form subclass |
| `NativeContextMenu` | Popup menus bound to a control, with a resolver hook |
| `ContextMenuRequest` | What a resolver receives: the control, the screen anchor, and whether the invocation came from the keyboard |
| `MenuSpecValidator` | Rejects duplicate mnemonics and malformed radio groups before any handle is allocated |
| `MenuTextFormatter` | Mnemonic parsing and Win32 accelerator-text formatting |
| `AccelConverter` | `Keys` → `ACCEL` mapping |
| `ListViewHeaderHitTest` | Whether a screen point is on a `ListView`'s column-header band |
| `NativeListView` | A `ListView` that is announced as a list rather than a broken table |

## Things it does on purpose

- **Mnemonic collisions are an error, not a shrug.** `MenuSpecValidator` throws if two items in
  the same menu level share a mnemonic. That is normally a silent accessibility defect that
  survives for years — most often introduced by a translator, in a language the author does not
  read. Failing loudly at startup is the point. It does mean localized menu text needs checking
  for duplicate `&` letters before it ships.
- **Display-only shortcuts.** Pass `shortcutKeys: null` to show accelerator text without
  registering the chord, for the cases where the focused control should keep the key —
  Ctrl+C in a text box, say.
- **Radio groups** render with a bullet (`MFT_RADIOCHECK`) and enforce mutual exclusion among
  siblings.
- **Right-to-left popups** get `MFT_RIGHTORDER | MFT_RIGHTJUSTIFY` per item, which
  `WS_EX_LAYOUTRTL` alone does not do.
- **Rebuild is cheap and expected.** Menus are rebuilt from a fresh spec on a language change,
  and context menus on every invocation. There is deliberately no `IsVisible` on an item.

## The ListView problem

A WinForms `ListView` in Details view is a real `SysListView32`, and screen readers have carried
dedicated handling for that control for decades: they read every column and let the user move
between them. WinForms then layers a UI Automation provider on top which reports the control as
`ControlType.Table` — and that provider is incomplete in a way that matters.

Measured on .NET 10:

| | Stock WinForms `ListView` |
| --- | --- |
| Control type | `Table` |
| Patterns advertised | Selection, Grid, MultipleView, Table |
| Grid dimensions | correct |
| Table column headers | correct |
| Each cell's `GridItemPattern` / `TableItemPattern` | correct row, column and header |
| **`GridPattern.GetItem(row, column)`** | **returns empty, typeless elements** |

Every piece of data is present, but `GetItem` — the call a screen reader makes to walk to a
cell — is broken. So the reader sees a table, enters table mode, asks for a cell, gets nothing
usable, and falls back to the row's `Name`, which is only the first column. Confirmed by ear with
JAWS, NVDA and Narrator: only the first column is read, and there is no way to reach the others.

`NativeListView` declines to serve that provider, so UI Automation falls back to the MSAA bridge
and screen readers use their own `SysListView32` support again. Nothing is lost at the MSAA
layer — role, name and per-row items are identical either way.

```csharp
// A drop-in for ListView. Nothing to configure.
var notes = new NativeListView { View = View.Details, FullRowSelect = true };
```

There is deliberately no property to switch the behavior off. It would not be a trade-off with a
defensible other side, and an application that wants the stock presentation already has a way to
ask for it: use `ListView`. If a future framework release fixes `GetItem`, prefer the stock
control — per-cell column headers and column navigation are genuinely better than a flat list,
when they work.

## Localization

The library owns no user-visible text. Menu labels, accelerator display strings — all of it
comes from the caller, so applications localize menus with whatever framework they already use
and this library imposes none:

```csharp
spec.AddMenu(_("&File"), file => file
    .Add(_("New &Note"), _("Ctrl+N"), Keys.Control | Keys.N, OnNewNote));
```

Its own strings are exception messages, and those stay in English — developers read them, in
logs and bug reports, and a translated exception message is one nobody can search for.

What it *does* do for localization is validate it. Two items in the same menu level sharing a
mnemonic is almost always a translation bug: introduced by a translator, in a language the
author does not read, and invisible until a keyboard user complains.
`MenuSpecValidator` turns that into a startup exception naming both items. Extracting this
library from its first application turned up five such collisions sitting in shipped German,
French and Ukrainian catalogs.

## Accessibility status, honestly

The menus are verified by ear with **JAWS**, **NVDA** and **Narrator**, and behave as expected
on all three.

`NativeListView` is verified on the same three: the stock WinForms behavior reads only the first
column on every one of them, and the native presentation reads all columns on every one of them.
Reports from other configurations are welcome.

RTL rendering is implemented but has not been verified against a real RTL locale.

## Building from source

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — the exact version is
  pinned in `global.json`
- Windows

### Clone and build

```
git clone https://github.com/Oire/winforms-native-controls.git
cd winforms-native-controls
dotnet build
```

### Build configurations

- **Debug** — full debug symbols, no optimization
- **Release** — optimized, no debug symbols

```
dotnet build -c Debug
dotnet build -c Release
```

Warnings are errors on CI, which is the warning-clean source of truth. Local builds are more
forgiving, so an environment-specific warning does not block you.

### Run the tests

```
dotnet test
```

Windows only, and not negotiable: the library P/Invokes `user32`, and the tests allocate real
`HMENU` handles and a real `Form` on a borrowed STA thread rather than mocking the interop away.

### Check formatting

```
dotnet format --verify-no-changes
```

CI runs this, so a formatting difference fails the build.

### Create the NuGet package

```
dotnet pack src/Oire.WinForms.NativeControls/Oire.WinForms.NativeControls.csproj -c Release
```

The version is not set in the project file: it comes from the git tags via
[GitVersion](https://gitversion.net/), so a release is cut by pushing a `v*` tag. That tag
triggers the release workflow, which packs, publishes to nuget.org through Trusted Publishing,
and creates the GitHub release. Note that nuget.org versions are immutable — the tag is the
point of no return.

## Project structure

At the repository root:

* `Oire.WinForms.NativeControls.slnx` — the solution, in the XML format, covering the library and the tests.
* `README.md` — this file.
* `CHANGELOG.md` — the release history, in [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
* `GitVersion.yml` — versioning configuration; the package version comes from git tags, not from the project file.
* `global.json` — the pinned SDK version.

The library lives in `src/Oire.WinForms.NativeControls/`:

* `Oire.WinForms.NativeControls.csproj` — the project file, holding all build and packaging configuration.
* `NativeMenuSpec.cs` — the fluent builder.
* `NativeMenuItemSpec.cs` — one entry in a menu: command, checkable, radio member, separator or submenu.
* `NativeMenuBar.cs` — the menu bar: `HMENU` tree, accelerator table, form subclass, `WM_COMMAND` routing.
* `NativeContextMenu.cs` — popup menus, the `WM_CONTEXTMENU` subclass and the resolver hook.
* `NativeMenuTree.cs` — the shared build, state-push and routing engine behind both wrappers.
* `MenuSpecValidator.cs` — mnemonic and radio-group validation.
* `MenuTextFormatter.cs` — mnemonic parsing and accelerator-text formatting.
* `AccelConverter.cs` — the `Keys` to `ACCEL` mapping.
* `MenuTrackingScope.cs` — guards a rebuild against a popup that is currently being tracked.
* `ListViewHeaderHitTest.cs` — `LVM_GETHEADER` / `HDM_HITTEST` for the column-header band.
* `NativeListView.cs` — the `ListView` subclass that declines WinForms' UI Automation provider.
* `Win32Interop.cs` — the P/Invoke surface and Win32 constants.

And alongside it, `tests/Oire.WinForms.NativeControls.Tests/` — the xUnit suite, including
`StaRunner.cs`, which runs the tests needing a real form handle on a dedicated STA thread,
because xUnit's own workers are MTA.

## Status

**0.x — the public API is not settled.** It has one production consumer so far; the shape
should survive contact with a second application before anything is called 1.0. Expect the
occasional breaking change until then, and pin a version if that matters to you.

Known limitations tracked for 1.0:

- The accelerator message filter gates on `Form.ActiveForm`, which is correct only when every
  secondary window is a `ShowDialog` modal. Applications with modeless child windows need the
  `Activated` / `Deactivate` model instead.
- No menu item images.
- No `WM_MENUSELECT` help text.
- No dynamic item insert/remove — rebuild the spec instead.
- No native tray menu. `NotifyIcon` exposes no hook for the keyboard-invoked tray menu, so that
  one genuinely needs its own design.

Intended next members, same disease and same cure: a native toolbar and a native status bar.

## Contributing

All contributions, big or small, are welcome. Please read the
[Oire contributing guide](https://github.com/Oire/.github/blob/master/CONTRIBUTING.md) and open
an issue before submitting a pull request, so everyone's work is easier to track.

Reports from screen-reader users are especially valuable — this library is tested by ear against
far fewer configurations than it runs on. If you report an announcement problem, say which screen
reader and version you used, and quote what you heard as closely as you can.

## License

Copyright © 2026 [André Polykanine](https://github.com/Menelion), [Oire Software](https://github.com/Oire), and contributors.  
Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.  
You may obtain a copy of the License at [http://www.apache.org/licenses/LICENSE-2.0].  
Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.  
See the License for the specific language governing permissions and limitations under the License.
