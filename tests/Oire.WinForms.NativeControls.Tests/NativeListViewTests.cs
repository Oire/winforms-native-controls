using System.Runtime.InteropServices;
using System.Windows.Forms;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// <see cref="NativeListView"/> exists for one reason: to be a real <c>SysListView32</c> rather
/// than a WinForms window class that screen readers do not recognize. The window class is
/// therefore the single most important thing asserted here — everything else is the model
/// staying in step with the control. Whether a reader announces it well is a listening test,
/// recorded in the README rather than asserted.
/// </summary>
public class NativeListViewTests {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, [Out] char[] buffer, int max);

    [Fact]
    public void IsNotAWinFormsListView() {
        StaRunner.Run(() => {
            using var list = new NativeListView();

            // The whole point: a ListView subclass cannot change its window class.
            list.Should().BeAssignableTo<Control>();
            list.Should().NotBeAssignableTo<ListView>();
        });
    }

    [Fact]
    public void CreatesARealSysListView32() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.ListHandle.Should().NotBe(IntPtr.Zero);

            var buffer = new char[64];
            var length = GetClassNameW(list.ListHandle, buffer, buffer.Length);
            length.Should().BeGreaterThan(0);

            // Not "WindowsForms10.SysListView32.app.0...", which is the name UI Automation and
            // NVDA both fail to match.
            new string(buffer, 0, length).Should().Be("SysListView32");
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

            list.Columns.Count.Should().Be(4);
            list.Items.Count.Should().Be(3);
            list.Items[1].Cells[3].Should().Be("2026-09-02 11:04");
            list.Items[1].Text.Should().Be("Voice Note 0001");

            list.Items[1].Selected = true;
            list.Items[1].Selected.Should().BeTrue();
            list.SelectedItems.Should().ContainSingle().Which.Should().BeSameAs(list.Items[1]);
        });
    }

    [Fact]
    public void TracksIndexesAsRowsMove() {
        StaRunner.Run(() => {
            using var list = Build();

            list.Items[0].Index.Should().Be(0);
            list.Items[2].Index.Should().Be(2);

            var third = list.Items[2];
            list.Items.RemoveAt(0);

            third.Index.Should().Be(1);
            list.Items.Count.Should().Be(2);
        });
    }

    [Fact]
    public void DetachesRowsThatLeaveTheControl() {
        StaRunner.Run(() => {
            using var list = Build();
            var removed = list.Items[0];

            list.Items.RemoveAt(0);

            removed.ListView.Should().BeNull();
            removed.Index.Should().Be(-1);

            // A detached row must not claim a state it cannot have.
            removed.Selected.Should().BeFalse();
        });
    }

    [Fact]
    public void RebuildsItselfAfterAHandleRecreation() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;
            var before = list.ListHandle;

            // What a right-to-left flip does to the form underneath the control.
            list.ForceRecreateHandle();
            _ = list.Handle;

            list.ListHandle.Should().NotBe(IntPtr.Zero);
            list.ListHandle.Should().NotBe(before);
            list.Columns.Count.Should().Be(4);
            list.Items.Count.Should().Be(3);
            list.Items[1].Cells[3].Should().Be("2026-09-02 11:04");
        });
    }

    [Fact]
    public void KeepsCellsAddressableWithoutAHandle() {
        StaRunner.Run(() => {
            using var list = Build();

            // Everything works detached; the handle only mirrors the model.
            list.Items[0].Cells[0] = "Renamed";
            list.Items[0].Text.Should().Be("Renamed");
            list.SelectedItems.Should().BeEmpty();
            list.FocusedItem.Should().BeNull();
        });
    }

    [Fact]
    public void ReportsColumnsWithTheirIndexes() {
        StaRunner.Run(() => {
            using var list = Build();

            list.Columns[0].Index.Should().Be(0);
            list.Columns[3].Index.Should().Be(3);
            list.Columns[3].Text.Should().Be("Modified");
            list.Columns[3].ListView.Should().BeSameAs(list);
        });
    }

    /// <summary>Exposes the protected recreation so a test can force one deliberately.</summary>
    private sealed class RecreatableListView: NativeListView {
        internal void ForceRecreateHandle() => RecreateHandle();
    }

    private static RecreatableListView Build() {
        var list = new RecreatableListView { AccessibleName = "Notes" };

        list.Columns.Add(new NativeListViewColumn("Title", 220));
        list.Columns.Add(new NativeListViewColumn("Position", 70));
        list.Columns.Add(new NativeListViewColumn("Created", 130));
        list.Columns.Add(new NativeListViewColumn("Modified", 130));

        list.Items.Add(new NativeListViewItem("Shopping list", "1", "2026-08-30 14:22", "2026-09-01 09:15"));
        list.Items.Add(new NativeListViewItem("Voice Note 0001", "2", "2026-09-02 11:04", "2026-09-02 11:04"));
        list.Items.Add(new NativeListViewItem("Meeting notes", "3", "2026-09-03 16:40", "2026-09-04 08:02"));
        return list;
    }
}
