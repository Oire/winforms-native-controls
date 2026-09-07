using System.Windows.Forms;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// Column properties read back from the control while the column is in one, and fall back to
/// what they were given while it is not. Both halves matter: the fallback is what a constructor
/// sees, since controls are populated before they are realized, and the read-back is what stops
/// the property drifting from the control afterwards.
/// </summary>
public class NativeListViewColumnTests {
    [Fact]
    public void Width_Detached_ReportsWhatItWasGiven() {
        var fixedWidth = new NativeListViewColumn("Title", 150);
        var auto = new NativeListViewColumn("Auto", NativeListViewColumn.AutoSizeToContent);

        fixedWidth.Width.Should().Be(150);
        auto.Width.Should().Be(NativeListViewColumn.AutoSizeToContent);
    }

    /// <summary>
    /// The usual order: columns are added in a constructor and the handle arrives later. A
    /// column in that state must still report the width it was given rather than zero, and an
    /// auto-sizing column must still report its sentinel rather than a resolved width.
    /// </summary>
    [Fact]
    public void Width_AddedBeforeTheHandleExists_StillReportsWhatItWasGiven() {
        StaRunner.Run(() => {
            using var list = new NativeListView();
            var fixedWidth = new NativeListViewColumn("Title", 150);
            var auto = new NativeListViewColumn("Auto", NativeListViewColumn.AutoSizeToHeader);

            list.Columns.Add(fixedWidth);
            list.Columns.Add(auto);

            list.ListHandle.Should().Be(IntPtr.Zero, "this is the pre-handle case");
            fixedWidth.Width.Should().Be(150);
            auto.Width.Should().Be(NativeListViewColumn.AutoSizeToHeader);
        });
    }

    [Fact]
    public void Width_OnceRealized_ReportsTheControlsWidth() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = new NativeListView();
            var column = new NativeListViewColumn("Title", 150);
            list.Columns.Add(column);
            list.Items.Add(new NativeListViewItem("row"));
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            column.Width.Should().Be(150);

            column.Width = 220;

            column.Width.Should().Be(220, "the control is the authority once there is one");
        });
    }

    [Fact]
    public void Alignment_Detached_ReportsWhatItWasGiven() {
        var column = new NativeListViewColumn("Words", 70, NativeColumnAlignment.Right);

        column.Alignment.Should().Be(NativeColumnAlignment.Right);
    }

    [Theory]
    [InlineData(NativeColumnAlignment.Left)]
    [InlineData(NativeColumnAlignment.Right)]
    [InlineData(NativeColumnAlignment.Center)]
    public void Alignment_OnceRealized_RoundTripsThroughTheControl(NativeColumnAlignment alignment) {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            // The second column: a report-mode list always draws column zero left-aligned.
            list.Columns[1].Alignment = alignment;

            list.Columns[1].Alignment.Should().Be(alignment);
        });
    }

    /// <summary>
    /// Column zero is the documented exception: the control stores and returns whatever format
    /// it is given but always draws that column left-aligned.
    /// </summary>
    [Fact]
    public void Alignment_OnColumnZero_IsStoredEvenThoughItIsNotDrawn() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.Columns[0].Alignment = NativeColumnAlignment.Right;

            list.Columns[0].Alignment.Should().Be(NativeColumnAlignment.Right);
        });
    }

    [Fact]
    public void SortOrder_Detached_DefaultsToNone() {
        var column = new NativeListViewColumn("Title", 100);

        column.SortOrder.Should().Be(NativeSortOrder.None);

        column.SortOrder = NativeSortOrder.Descending;

        column.SortOrder.Should().Be(NativeSortOrder.Descending, "with no control, the field answers");
    }

    [Theory]
    [InlineData(NativeSortOrder.None)]
    [InlineData(NativeSortOrder.Ascending)]
    [InlineData(NativeSortOrder.Descending)]
    public void SortOrder_OnceRealized_RoundTripsThroughTheHeader(NativeSortOrder order) {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.Columns[1].SortOrder = order;

            // Read back from the header's format word, not from a remembered field.
            list.Columns[1].SortOrder.Should().Be(order);
        });
    }

    /// <summary>
    /// The arrow and the alignment share one Win32 format word, so each must survive the other
    /// being written. This is the pairing a naive implementation gets wrong in one direction.
    /// </summary>
    [Fact]
    public void SortOrderAndAlignment_SetInEitherOrder_BothSurvive() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var first = list.Columns[1];
            first.SortOrder = NativeSortOrder.Descending;
            first.Alignment = NativeColumnAlignment.Center;

            first.SortOrder.Should().Be(NativeSortOrder.Descending);
            first.Alignment.Should().Be(NativeColumnAlignment.Center);

            var second = list.Columns[2];
            second.Alignment = NativeColumnAlignment.Right;
            second.SortOrder = NativeSortOrder.Ascending;

            second.Alignment.Should().Be(NativeColumnAlignment.Right);
            second.SortOrder.Should().Be(NativeSortOrder.Ascending);
        });
    }

    [Fact]
    public void Text_SetToNull_Throws() {
        var column = new NativeListViewColumn("Title", 100);

        var construct = () => new NativeListViewColumn(null!, 100);
        var assign = () => column.Text = null!;

        construct.Should().Throw<ArgumentNullException>();
        assign.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Text_OnceRealized_ReachesTheHeader() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.Columns[1].Text = "Renamed";

            list.Columns[1].Text.Should().Be("Renamed");
            list.Columns[1].ToString().Should().Be("Renamed");
        });
    }

    private static NativeListView Build() {
        var list = new NativeListView { AccessibleName = "Notes" };

        list.Columns.Add(new NativeListViewColumn("Title", 200));
        list.Columns.Add(new NativeListViewColumn("Words", 70));
        list.Columns.Add(new NativeListViewColumn("Modified", 130));
        list.Items.Add(new NativeListViewItem("Shopping list", "42", "2026-09-01 09:15"));
        return list;
    }
}
