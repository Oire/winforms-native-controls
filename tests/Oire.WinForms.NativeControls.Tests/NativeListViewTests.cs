using System.Windows.Forms;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// <see cref="NativeListView"/> changes what the control answers to exactly one Win32 message,
/// so what is worth testing here is that it remains an ordinary, working <see cref="ListView"/>
/// in every other respect. Whether a screen reader announces it correctly is a listening test,
/// recorded in the README rather than asserted here.
/// </summary>
public class NativeListViewTests {
    [Fact]
    public void IsADropInForListView() {
        StaRunner.Run(() => {
            using var list = new NativeListView();

            list.Should().BeAssignableTo<ListView>();
        });
    }

    [Fact]
    public void CarriesColumnsItemsAndSelectionOnceRealized() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.IsHandleCreated.Should().BeTrue();
            list.Columns.Count.Should().Be(4);
            list.Items.Count.Should().Be(3);
            list.Items[1].Selected.Should().BeTrue();
            list.Items[1].SubItems[3].Text.Should().Be("2026-09-02 11:04");
            list.AccessibleName.Should().Be("Notes");
        });
    }

    [Fact]
    public void SurvivesAHandleRecreation() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = list.Handle;
            var before = list.Handle;

            list.ForceRecreateHandle();

            list.Handle.Should().NotBe(before);
            list.Columns.Count.Should().Be(4);
            list.Items.Count.Should().Be(3);
            list.Items[1].SubItems[3].Text.Should().Be("2026-09-02 11:04");
        });
    }

    /// <summary>Exposes the protected recreation so the test can force one deliberately.</summary>
    private sealed class RecreatableListView: NativeListView {
        internal void ForceRecreateHandle() => RecreateHandle();
    }

    private static RecreatableListView Build() {
        var list = new RecreatableListView {
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
