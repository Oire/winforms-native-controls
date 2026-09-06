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

    [Fact]
    public void AsksForARealSizeSoLayoutPanelsCanMeasureIt() {
        StaRunner.Run(() => {
            using var list = Build();

            // A control that asks for nothing is not merely unopinionated: a TableLayoutPanel
            // divides a row-spanning neighbor's height by what each row asks for, so zero here
            // hands the whole share to an auto-sized row, which grows and pushes the list down.
            var preferred = list.GetPreferredSize(new Size(900, 900));

            preferred.Width.Should().BeGreaterThan(0);
            preferred.Height.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void DerivesThatSizeFromTheFontRatherThanFixingItInPixels() {
        StaRunner.Run(() => {
            using var list = Build();

            list.Font = new Font(list.Font.FontFamily, 8F);
            var smaller = list.DefaultSizeForTests;

            list.Font = new Font(list.Font.FontFamily, 20F);
            var bigger = list.DefaultSizeForTests;

            // A constant would be right at exactly one scaling and one font size.
            bigger.Height.Should().BeGreaterThan(smaller.Height);
            bigger.Width.Should().BeGreaterThan(smaller.Width);
        });
    }

    /// <summary>
    /// The control is a <see cref="Control"/>, whose background default is the dialog gray.
    /// For a data surface that is wrong, and a WinForms <c>ListView</c> makes the same
    /// correction to the window colors.
    /// </summary>
    [Fact]
    public void DefaultsToTheWindowColorsRatherThanTheDialogGray() {
        StaRunner.Run(() => {
            using var list = new NativeListView();

            list.BackColor.Should().Be(SystemColors.Window);
            list.ForeColor.Should().Be(SystemColors.WindowText);
        });
    }

    /// <summary>
    /// The list window paints itself, so a color set on the managed control means nothing
    /// unless it is pushed across. Read back from the control rather than trusted.
    /// </summary>
    [Fact]
    public void PushesColorsIntoTheListWindow() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.BackColor = Color.FromArgb(0x10, 0x20, 0x30);
            list.ForeColor = Color.FromArgb(0x40, 0x50, 0x60);

            // COLORREF is 0x00BBGGRR, the reverse of the usual order.
            SendMessageW(list.ListHandle, LVM_GETBKCOLOR, IntPtr.Zero, IntPtr.Zero)
                .Should().Be(0x00302010);
            SendMessageW(list.ListHandle, LVM_GETTEXTCOLOR, IntPtr.Zero, IntPtr.Zero)
                .Should().Be(0x00605040);
        });
    }

    /// <summary>
    /// The MSAA proxy reports the list window's text as the control's name, so clearing the
    /// name has to reach the window too - otherwise a reader goes on announcing a name the
    /// application has already taken away.
    /// </summary>
    [Fact]
    public void ClearingTheAccessibleNameReachesTheListWindow() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            WindowTextOf(list.ListHandle).Should().Be("Notes");

            list.AccessibleName = null;

            WindowTextOf(list.ListHandle).Should().BeEmpty();
        });
    }

    /// <summary>
    /// A theme change - light to dark, or into high contrast - arrives long after the window
    /// was created and its colors first pushed. Without a re-push the list keeps the old
    /// theme's colors while the rest of the application follows the new one.
    /// </summary>
    /// <remarks>
    /// Driven by poking the list window behind the control's back rather than by switching the
    /// application's color mode, which is process-wide and would leak into every other test.
    /// </remarks>
    [Fact]
    public void RepushesColorsWhenTheSystemThemeChanges() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.BackColor = Color.FromArgb(0x10, 0x20, 0x30);

            // Drift, as a theme change would leave behind.
            _ = SendMessageW(list.ListHandle, LVM_SETBKCOLOR, IntPtr.Zero, 0x00FFFFFF);
            SendMessageW(list.ListHandle, LVM_GETBKCOLOR, IntPtr.Zero, IntPtr.Zero)
                .Should().Be(0x00FFFFFF);

            list.RaiseSystemColorsChanged();

            SendMessageW(list.ListHandle, LVM_GETBKCOLOR, IntPtr.Zero, IntPtr.Zero)
                .Should().Be(0x00302010);
        });
    }

    /// <summary>
    /// Alignment is settable after construction, like every other column property.
    /// </summary>
    [Fact]
    public void ColumnAlignmentIsSettableAfterConstruction() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            // Column zero is forced left-aligned by the control, so this uses the second.
            var column = list.Columns[1];
            column.Alignment.Should().Be(NativeColumnAlignment.Left);

            column.Alignment = NativeColumnAlignment.Right;

            column.Alignment.Should().Be(NativeColumnAlignment.Right);
            (ColumnFormatOf(list, 1) & ListViewInterop.LVCFMT_JUSTIFYMASK)
                .Should().Be(ListViewInterop.LVCFMT_RIGHT);
        });
    }

    /// <summary>
    /// The sort arrow lives in the same format word as the alignment bits, so a naive write
    /// would clear an arrow that was already there. Both have to survive the other.
    /// </summary>
    [Fact]
    public void SettingAlignmentKeepsTheSortArrow() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var column = list.Columns[1];
            column.SortOrder = NativeSortOrder.Ascending;
            (ColumnFormatOf(list, 1) & ListViewInterop.HDF_SORTUP).Should().NotBe(0);

            column.Alignment = NativeColumnAlignment.Center;

            var format = ColumnFormatOf(list, 1);
            (format & ListViewInterop.LVCFMT_JUSTIFYMASK).Should().Be(ListViewInterop.LVCFMT_CENTER);
            (format & ListViewInterop.HDF_SORTUP).Should().NotBe(0, "the arrow shares the format word");
        });
    }

    /// <summary>The live format word of a column, read back out of the control.</summary>
    private static int ColumnFormatOf(NativeListView list, int index) {
        var native = new ListViewInterop.LVCOLUMNW { Mask = ListViewInterop.LVCF_FMT };
        ListViewInterop.SendMessageW(list.ListHandle, ListViewInterop.LVM_GETCOLUMNW, index, ref native)
            .Should().NotBe(IntPtr.Zero);
        return native.Fmt;
    }

    private const uint LVM_SETBKCOLOR = 0x1000 + 1;
    private const uint LVM_GETBKCOLOR = 0x1000 + 0;
    private const uint LVM_GETTEXTCOLOR = 0x1000 + 35;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] buffer, int max);

    private static string WindowTextOf(IntPtr handle) {
        var buffer = new char[256];
        var length = GetWindowTextW(handle, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }

    /// <summary>Exposes the protected recreation so a test can force one deliberately.</summary>
    private sealed class RecreatableListView: NativeListView {
        internal void ForceRecreateHandle() => RecreateHandle();

        /// <summary>Raises the protected theme-change hook a real theme switch would raise.</summary>
        internal void RaiseSystemColorsChanged() => OnSystemColorsChanged(EventArgs.Empty);

        /// <summary>The font-derived default, which is otherwise protected.</summary>
        internal Size DefaultSizeForTests => DefaultSize;
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
