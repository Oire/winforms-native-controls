using System.Windows.Forms;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// <see cref="NativeListView"/> only changes what the control answers to one Win32 message, so
/// what is worth testing is that it stays a working ListView across the handle recreation that
/// a mode change forces. Whether a screen reader announces it correctly is a listening test,
/// recorded in the README rather than here.
/// </summary>
public class NativeListViewTests {
    [Fact]
    public void DefaultsToListMode() {
        StaRunner.Run(() => {
            using var list = new NativeListView();

            list.AccessibilityMode.Should().Be(ListAccessibilityMode.List,
                "presenting as a native list is the reason the control exists");
        });
    }

    [Fact]
    public void ChangingMode_PreservesColumnsItemsAndSelection() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.IsHandleCreated.Should().BeTrue();
            var before = list.Handle;

            list.AccessibilityMode = ListAccessibilityMode.Table;

            // The handle is deliberately recreated: assistive technology caches what a window
            // reported the first time it asked.
            list.Handle.Should().NotBe(before);
            list.Columns.Count.Should().Be(4);
            list.Items.Count.Should().Be(3);
            list.Items[1].Selected.Should().BeTrue();
            list.Items[1].SubItems[3].Text.Should().Be("2026-09-02 11:04");
        });
    }

    [Fact]
    public void SettingTheSameMode_DoesNotRecreateTheHandle() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = list.Handle;
            var before = list.Handle;

            list.AccessibilityMode = ListAccessibilityMode.List;

            list.Handle.Should().Be(before);
        });
    }

    [Fact]
    public void ModeCanBeSetBeforeTheHandleExists() {
        StaRunner.Run(() => {
            using var list = new NativeListView();

            var act = () => list.AccessibilityMode = ListAccessibilityMode.Table;

            act.Should().NotThrow();
            list.AccessibilityMode.Should().Be(ListAccessibilityMode.Table);
            list.IsHandleCreated.Should().BeFalse();
        });
    }

    private static NativeListView Build() {
        var list = new NativeListView {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            UseCompatibleStateImageBehavior = false,
            AccessibleName = "Notes",
        };

        list.Columns.Add("Title", 220);
        list.Columns.Add("Position", 70);
        list.Columns.Add("Created", 130);
        list.Columns.Add("Modified", 130);

        list.Items.Add(new ListViewItem(["Shopping list", "1", "2026-08-30 14:22", "2026-09-01 09:15"]));
        list.Items.Add(new ListViewItem(["Voice Note 0001", "2", "2026-09-02 11:04", "2026-09-02 11:04"]));
        list.Items.Add(new ListViewItem(["Meeting notes", "3", "2026-09-03 16:40", "2026-09-04 08:02"]));
        list.Items[1].Selected = true;
        return list;
    }
}
