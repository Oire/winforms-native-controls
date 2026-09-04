# Oire.WinForms.NativeControls

Win32-native replacements for the WinForms controls that screen readers handle poorly — starting with menus.

WinForms reimplemented its menus in managed code, and screen readers have paid for it ever
since. `MenuStrip` and `ContextMenuStrip` announce generically, render submenus poorly, and are
sluggish on first open no matter how much you pre-warm them. This puts menus back on real Win32
`HMENU` handles, where every screen reader has understood them for thirty years.

## Install

```
dotnet add package Oire.WinForms.NativeControls
```

Targets `net10.0-windows`, AnyCPU.

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

## What's in the box

| Type | Purpose |
| --- | --- |
| `NativeMenuSpec` / `NativeMenuItemSpec` | Declarative menu description — items, submenus, separators, checkables, radio groups |
| `NativeMenuBar` | Owns the `HMENU`, the accelerator table and the form subclass |
| `NativeContextMenu` | Popup menus bound to a control, with a resolver hook |
| `MenuSpecValidator` | Rejects duplicate mnemonics and malformed radio groups before any handle is allocated |
| `MenuTextFormatter` | Mnemonic parsing and Win32 accelerator-text formatting |
| `AccelConverter` | `Keys` → `ACCEL` mapping |
| `ListViewHeaderHitTest` | Whether a screen point is on a `ListView`'s column-header band |

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

Verified by ear with **JAWS**. NVDA and Narrator have not been tested — the design should suit
them, since native menus are exactly what UI Automation and MSAA both understand best, but
nobody has confirmed it and this README will not claim otherwise until someone has. Reports
welcome.

RTL rendering is implemented but has not been verified against a real RTL locale.

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

Intended next members, same disease and same cure: a native toolbar and a native status bar.

## License

Apache 2.0. See [LICENSE](LICENSE).
