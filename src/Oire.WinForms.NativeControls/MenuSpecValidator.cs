namespace Oire.WinForms.NativeControls;

/// <summary>
/// Pre-build checks shared by <see cref="NativeMenuBar"/> and <see cref="NativeContextMenu"/>,
/// so both reject a malformed spec identically and before any <c>HMENU</c> is allocated.
/// </summary>
public static class MenuSpecValidator {
    /// <summary>
    /// Validates mnemonic uniqueness and radio-group integrity across the whole spec tree.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Two items in the same menu level share a mnemonic, a radio group spans more than one
    /// parent menu, or a group starts with more than one item checked.
    /// </exception>
    public static void Validate(NativeMenuSpec spec) {
        ArgumentNullException.ThrowIfNull(spec);

        // Radio groups are checked tree-wide rather than per level, because "members must be
        // siblings" can only be violated by comparing two different levels.
        var groupOwners = new Dictionary<string, GroupOwner>(StringComparer.Ordinal);
        ValidateLevel(spec.Items, levelName: "the top level", groupOwners);
    }

    private static void ValidateLevel(
        IReadOnlyList<NativeMenuItemSpec> items,
        string levelName,
        Dictionary<string, GroupOwner> groupOwners) {
        CheckMnemonics(items, levelName);
        CheckRadioGroups(items, levelName, groupOwners);

        foreach (var item in items) {
            if (item.Children is { } children) {
                ValidateLevel(children, $"submenu '{MenuTextFormatter.StripMnemonic(item.Text)}'", groupOwners);
            }
        }
    }

    private static void CheckMnemonics(IReadOnlyList<NativeMenuItemSpec> items, string levelName) {
        var seen = new Dictionary<char, string>();
        foreach (var item in items) {
            if (item.IsSeparator) {
                continue;
            }

            if (MenuTextFormatter.ExtractMnemonic(item.Text) is not { } mnemonic) {
                continue;
            }

            if (seen.TryGetValue(mnemonic, out var previousText)) {
                throw new InvalidOperationException(
                    $"Mnemonic '{mnemonic}' is used twice in {levelName}: '{previousText}' and '{item.Text}'.");
            }

            seen[mnemonic] = item.Text;
        }
    }

    private static void CheckRadioGroups(
        IReadOnlyList<NativeMenuItemSpec> items,
        string levelName,
        Dictionary<string, GroupOwner> groupOwners) {
        var checkedCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in items) {
            if (item.RadioGroup is not { } group) {
                continue;
            }

            if (groupOwners.TryGetValue(group, out var owner) && !ReferenceEquals(owner.Items, items)) {
                throw new InvalidOperationException(
                    $"Radio group '{group}' spans more than one menu: it appears in {owner.LevelName} and in {levelName}. " +
                    "Radio-group members must be direct siblings.");
            }

            groupOwners[group] = new GroupOwner(items, levelName);
            checkedCounts.TryGetValue(group, out var count);
            checkedCounts[group] = count + (item.IsChecked ? 1 : 0);
        }

        foreach (var (group, count) in checkedCounts) {
            if (count > 1) {
                throw new InvalidOperationException(
                    $"Radio group '{group}' in {levelName} starts with {count} items checked; at most one may be checked.");
            }
        }
    }

    /// <summary>Menu level a radio group was first seen in. Identity is the item list itself,
    /// because two different submenus can share a display name.</summary>
    private sealed record GroupOwner(IReadOnlyList<NativeMenuItemSpec> Items, string LevelName);
}
