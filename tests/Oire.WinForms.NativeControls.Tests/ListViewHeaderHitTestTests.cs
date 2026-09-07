using System.Windows.Forms;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// The header hit test is what lets one control carry a column menu and a row menu. It answers
/// from the header window itself rather than from any WinForms geometry, so these tests put a
/// real control on a real form and ask about real screen points.
/// </summary>
public class ListViewHeaderHitTestTests {
    [Fact]
    public void IsOnHeader_NullListView_Throws() {
        var stock = () => ListViewHeaderHitTest.IsOnHeader((ListView)null!, Point.Empty);
        var native = () => ListViewHeaderHitTest.IsOnHeader((NativeListView)null!, Point.Empty);

        stock.Should().Throw<ArgumentNullException>();
        native.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Before the control has a window there is no header to hit, and asking must be a plain
    /// false rather than a throw - a resolver runs on whatever state the control is in.
    /// </summary>
    [Fact]
    public void IsOnHeader_WithoutAHandle_ReturnsFalse() {
        StaRunner.Run(() => {
            using var stock = new ListView { View = View.Details };
            using var native = new NativeListView();

            ListViewHeaderHitTest.IsOnHeader(stock, new Point(10, 10)).Should().BeFalse();
            ListViewHeaderHitTest.IsOnHeader(native, new Point(10, 10)).Should().BeFalse();
        });
    }

    [Fact]
    public void IsOnHeader_PointInsideTheHeaderBand_ReturnsTrue() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = BuildNative();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var header = HeaderBoundsOf(list);
            header.IsEmpty.Should().BeFalse("the first row must sit below a header band");

            // Well inside the band, and inside the first column rather than on its divider.
            var inside = list.PointToScreen(new Point(header.Left + 20, header.Top + (header.Height / 2)));

            ListViewHeaderHitTest.IsOnHeader(list, inside).Should().BeTrue();
        });
    }

    [Fact]
    public void IsOnHeader_PointOverARow_ReturnsFalse() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = BuildNative();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var row = list.GetItemBounds(0);
            row.IsEmpty.Should().BeFalse();

            var overRow = list.PointToScreen(new Point(row.Left + 5, row.Top + (row.Height / 2)));

            ListViewHeaderHitTest.IsOnHeader(list, overRow).Should().BeFalse();
        });
    }

    [Fact]
    public void IsOnHeader_PointFarOutsideTheControl_ReturnsFalse() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = BuildNative();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            ListViewHeaderHitTest.IsOnHeader(list, new Point(-5000, -5000)).Should().BeFalse();
        });
    }

    /// <summary>
    /// The stock overload exists so an application migrating one control at a time can use the
    /// same helper for both. It has to answer without throwing on a realized WinForms list.
    /// </summary>
    [Fact]
    public void IsOnHeader_StockListViewInDetailsView_FindsItsHeader() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var stock = new ListView { View = View.Details, Dock = DockStyle.Fill };
            stock.Columns.Add("Title", 120);
            stock.Items.Add(new ListViewItem("Shopping list"));
            form.Controls.Add(stock);
            _ = form.Handle;
            _ = stock.Handle;

            var row = stock.Items[0].Bounds;
            row.Top.Should().BeGreaterThan(0, "a header band sits above the first row");

            var onHeader = stock.PointToScreen(new Point(row.Left + 20, row.Top / 2));

            ListViewHeaderHitTest.IsOnHeader(stock, onHeader).Should().BeTrue();
        });
    }

    /// <summary>
    /// The documented claim is that this returns false in any view other than Details, where
    /// there is no header window. Worth asserting rather than assuming: the header window is
    /// not necessarily destroyed when the view changes.
    /// </summary>
    [Fact]
    public void IsOnHeader_StockListViewNotInDetailsView_ReturnsFalse() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var stock = new ListView { View = View.LargeIcon, Dock = DockStyle.Fill };
            stock.Columns.Add("Title", 120);
            stock.Items.Add(new ListViewItem("Shopping list"));
            form.Controls.Add(stock);
            _ = form.Handle;
            _ = stock.Handle;

            // Every point across the top band, where a header would be if there were one.
            for (var y = 0; y < 24; y += 4) {
                ListViewHeaderHitTest.IsOnHeader(stock, stock.PointToScreen(new Point(20, y)))
                    .Should().BeFalse($"there is no header in {stock.View} view (y={y})");
            }
        });
    }

    /// <summary>The header band, derived from the gap above the first row.</summary>
    private static Rectangle HeaderBoundsOf(NativeListView list) {
        var first = list.GetItemBounds(0);
        return first.IsEmpty || first.Top <= 0
            ? Rectangle.Empty
            : new Rectangle(0, 0, list.ClientSize.Width, first.Top);
    }

    private static NativeListView BuildNative() {
        var list = new NativeListView { AccessibleName = "Notes" };
        list.Columns.Add(new NativeListViewColumn("Title", 160));
        list.Columns.Add(new NativeListViewColumn("Modified", 120));
        list.Items.Add(new NativeListViewItem("Shopping list", "2026-09-01 09:15"));
        list.Items.Add(new NativeListViewItem("Bread", "2026-08-30 14:22"));
        return list;
    }
}
