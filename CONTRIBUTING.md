# Contributing

Thanks for looking. Issues and pull requests are welcome, especially reports from screen-reader
users — that is the whole point of this library, and it is tested by ear against far fewer
configurations than it runs on.

## Before you start

Open an issue first for anything non-trivial. The scope here is narrow on purpose (see below),
and it is kinder to find out before you write the code.

## Building

```
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Windows only — the library P/Invokes `user32`, and the tests allocate real `HMENU` handles and
a real `Form` on an STA thread. CI runs exactly these three commands.

The library and the tests both target `net10.0-windows`.

## Scope

This library replaces WinForms controls that Microsoft reimplemented in managed code, where
that reimplementation hurt accessibility. Menus today; a native toolbar and status bar next.

It is **not** a general control library. A control that already sits on a real Win32 control —
`ListView`, `TreeView`, `ComboBox` and friends — has nothing to replace, and wrapping it again
would only add a layer between the user and thirty years of screen-reader compatibility.

## The library owns no user-visible text

This is the rule that keeps the library out of the localization business, and it is worth
stating plainly because it is easy to break by accident.

Every string a user hears comes from the caller: menu labels, accelerator display text,
everything. The library's own strings are exception messages, and those stay in English —
they are read by developers, in logs and bug reports, and a translated exception message is one
nobody can search for.

If a feature needs to *generate* user-visible text — formatting a `Keys` value as "Ctrl+N",
say — it must take a caller-supplied delegate rather than shipping strings of its own. That
keeps applications free to localize with whatever framework they already use, and keeps this
library from imposing one on them.

## What the library does do for localization

It validates it. `MenuSpecValidator` rejects two items in the same menu level sharing a
mnemonic, which is overwhelmingly a translation bug — introduced by a translator, in a language
the author does not read, and otherwise invisible until a keyboard user complains. Extracting
this library from its first application turned up five such collisions sitting in shipped
German, French and Ukrainian catalogs.

## Style

`.editorconfig` covers it, and `dotnet format` enforces it. Beyond that: comments explain *why*,
not *what*, and interop declarations say what the Win32 documentation says, not what we hope it
says.

## Accessibility changes

Any change that could affect what a screen reader announces needs to be verified by ear, and the
pull request should say which screen reader and version you used. "Should be fine" is not
verification — this library exists because a plausible-looking accessibility assumption was
wrong.
