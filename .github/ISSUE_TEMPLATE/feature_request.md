---
name: Feature request
about: Suggest an idea or improvement
title: ''
labels: enhancement
assignees: Menelion
---

## Problem

What are you trying to do that this library does not currently support? Describe the use case
rather than a specific solution.

## Proposed solution

What you would like to see. API sketches are welcome.

## Alternatives considered

Any workarounds or other approaches you have thought about.

## Additional context

Links to relevant Win32 or UI Automation documentation, or related issues.

## Note on scope

This library replaces WinForms controls that Microsoft reimplemented in managed code, where
that reimplementation hurt accessibility - menus today, toolbars and status bars next. It is
not a general control library. Requests for controls that already sit on a real Win32 control
(`ListView`, `TreeView`, `ComboBox` and friends) are usually out of scope, because there is
nothing there to replace.
