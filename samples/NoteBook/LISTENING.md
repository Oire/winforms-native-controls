# Verifying this library by ear

This is the acceptance test for the claims in the README, and it is written for whoever is
evaluating, reviewing or contributing to this library — not for screen reader users
specifically. The claims here are all of the form "a screen reader announces this correctly",
and neither the compiler nor the 136 automated tests can check a single one of them. Only
listening can.

**You do not need to be a screen reader user to run this.** You need a screen reader running
for a few minutes, which is a free download and no setup. If you have never used one, the three
commands worth knowing are below. Taking fifteen minutes to walk this script is the fastest way
to find out whether the library does what it says.

## Getting set up

Any one of these:

- **Narrator** ships with Windows. Press **Ctrl+Windows+Enter** to start and stop it. Nothing
  to install, so it is the quickest way to begin.
- **NVDA** is free and open source, from nvaccess.org. Press **Insert+Q** to quit it.
- **JAWS** is commercial, with a trial mode that runs for forty minutes at a time.

Each of the three has caught problems the other two did not, so a report that names which one
you used is worth far more than one that does not.

Two things to know before you start. Press **Ctrl** to shut the reader up mid-sentence — you
will want this constantly. And the exact wording differs between readers and versions, so what
matters at each step is that the right *kind* of thing is announced, not that the phrasing
matches this document.

Then run the sample, from the root of the repository:

```
dotnet run --project samples/NoteBook
```

Start the screen reader before the application.

Worth running twice: once with the system in its normal theme, once in dark mode or a
high-contrast theme, since the list takes its colors and visual style from that.

## 1. The menu bar is a menu bar

- Press **Alt**. You should hear that a *menu bar* has focus, and that you are on **File**.
- Press **Right arrow** twice. You should hear **View**, then **Help** — each announced as a
  menu, not as a generic button or pane.
- On **View**, press **Down arrow**. You should hear **Show Modified column**, and that it is
  a *checked* item. Press **Enter** to choose it, then reopen **View**: it should now be
  announced as unchecked. A menu that keeps announcing the old state is the failure here.
- Reopen **View**, arrow down to **Language** and press **Right arrow** to open it. You should
  hear **English** announced as a *radio* item, and as selected. That is the distinction a
  managed menu strip usually loses, and the reason radio groups are in this library at all.
- Press **Escape** twice to close the menu. You should now be told where focus went — the
  control you were on before the menu opened. Silence here is a bug: closing a native menu
  moves no focus and therefore fires no focus event of its own.

## 2. Accelerators fire from anywhere

- Put focus in the **categories tree**, then press **Ctrl+N**. Focus should land in the note
  text box. Start from the tree rather than from the text box: Ctrl+N focuses the text box, so
  starting there would look identical whether the accelerator worked or not.
- Try it again from inside the **notes list**, which is the harder case — that is a window
  WinForms does not own, so the keystroke has to reach the form's accelerator table anyway.

## 3. The list reads every column

This is the reason the library exists.

- Press **Tab** until you reach the notes list. You should hear it named **Notes**, announced
  as a *list* with a row count, and you should hear the first row rather than silence.
- Press **Down arrow**. You should hear the whole row: the title, the word count and the
  modified date — not the title alone. A stock WinForms list view reads only the first column
  here, on every reader tested.
- Use your reader's column-navigation command to move across the row and confirm each cell is
  reachable and named with its column header.

## 4. Tab leaves the list correctly

The list is a real `SysListView32` inside a WinForms container, so Tab has to get out of a
window that WinForms does not own. This is the step most likely to fail.

- From inside the list, press **Tab**. Focus should land on the note text box, and you should
  hear it named.
- Press **Shift+Tab** twice. Focus should go back through the list to the categories tree.
- Confirm that arrow keys *inside* the list move between rows rather than moving to another
  control.

## 5. The context menu, from the keyboard and the mouse

- With focus on a list row, press **Shift+F10** or the **Applications** key. You should hear a
  *popup menu* open, and it should appear next to the focused row rather than at the corner of
  the control.
- Press **Escape**. You should again be told where focus returned to.
- Right-click a **column header**. A different menu should open — the one about columns.
- Right-click a **row**. The note menu should open instead.

## 6. A language change, and right to left

This is the part of the library with the least real-world verification, and the reason the
sample ships a Hebrew catalog rather than only a layout switch: mirroring English text proves
very little.

- Open **View**, then **Language**, and choose **Hebrew**. The two entries are a radio group,
  so listen for the selected one being announced as selected rather than as a checkbox.
- Everything reloads: the menu bar, the column headers, the category tree and the rows. The
  labels and column headers are relabeled in place; the menu bar is rebuilt from a fresh spec,
  because the per-item right-to-left flags are fixed when the menu is built.
- Press **Alt** and walk the menus again. Submenus should open toward the left, and the arrow
  key that opens one should be the key pointing that way.
- The mnemonics are Hebrew letters now. If you have a Hebrew keyboard layout installed, press
  **Alt** and then a marked letter and confirm the item activates; without one, arrow to the
  items instead. Had two items in the same menu been given the same Hebrew letter, the
  library would have thrown at the moment you switched language rather than showing a menu with
  two items answering to one key — which is the point of that check.
- Tab into the list. Confirm the header, the row text and the column order all read
  right to left, and that arrow keys still move between rows rather than out of the list.
- Switch back to **English** the same way and confirm everything returns.

Worth doing this on a machine whose Windows display language is Hebrew as well as on an
English one; the two are not the same test.

## Reporting

Say which screen reader and version, which step, what you heard quoted as closely as you can,
and what you expected instead. Quoting the garbled version is more useful than tidying it up.

A report from someone who had never run a screen reader before is not worth less than one from
a daily user. It is often worth more: a first-time listener notices the announcement that makes
no sense, where someone fluent has long since learned to work around it.
