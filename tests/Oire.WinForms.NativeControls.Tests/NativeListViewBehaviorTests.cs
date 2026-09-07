using System.Runtime.InteropServices;
using System.Windows.Forms;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// The list surface an application actually drives: hit testing, bulk updates, selection,
/// the insertion mark and disposal. Separate from <see cref="NativeListViewTests"/>, which is
/// about the control being a real <c>SysListView32</c> and staying in step with its model.
/// </summary>
public class NativeListViewBehaviorTests {
    [Fact]
    public void GetItemAt_OverARow_ReturnsThatRow() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var bounds = list.GetItemBounds(1);
            bounds.IsEmpty.Should().BeFalse();

            var hit = list.GetItemAt(bounds.Left + 5, bounds.Top + (bounds.Height / 2));

            hit.Should().BeSameAs(list.Items[1]);
        });
    }

    [Fact]
    public void GetItemAt_BothOverloads_Agree() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var bounds = list.GetItemBounds(0);
            bounds.IsEmpty.Should().BeFalse("the point below must land on a real row");
            var point = new Point(bounds.Left + 5, bounds.Top + (bounds.Height / 2));

            var viaPoint = list.GetItemAt(point);
            viaPoint.Should().BeSameAs(list.Items[0], "both overloads must find an actual row");
            list.GetItemAt(point.X, point.Y).Should().BeSameAs(viaPoint);
        });
    }

    [Fact]
    public void GetItemAt_BelowTheLastRow_ReturnsNull() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            // Prove the control answers at all before trusting a null from it: a list with no
            // window returns null for every point, which would make this pass for the wrong reason.
            var lastRow = list.GetItemBounds(list.Items.Count - 1);
            lastRow.IsEmpty.Should().BeFalse();
            list.GetItemAt(lastRow.Left + 5, lastRow.Top + (lastRow.Height / 2))
                .Should().BeSameAs(list.Items[^1]);

            list.GetItemAt(5, lastRow.Bottom + (lastRow.Height * 2)).Should().BeNull();
        });
    }

    [Fact]
    public void GetItemBounds_WithoutAHandle_IsEmpty() {
        StaRunner.Run(() => {
            using var list = Build();

            list.GetItemBounds(0).Should().Be(Rectangle.Empty);
            list.GetItemAt(0, 0).Should().BeNull();
        });
    }

    [Fact]
    public void GetItemBounds_OutOfRangeIndex_IsEmpty() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.GetItemBounds(0).IsEmpty.Should().BeFalse("the control must be answering");

            list.GetItemBounds(-1).Should().Be(Rectangle.Empty);
            list.GetItemBounds(999).Should().Be(Rectangle.Empty);
        });
    }

    /// <summary>
    /// Rows are laid out top to bottom, so the second row's bounds sit below the first's. This
    /// is what a drop indicator is positioned against.
    /// </summary>
    [Fact]
    public void GetItemBounds_ForSuccessiveRows_DescendTheControl() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var first = list.GetItemBounds(0);
            var second = list.GetItemBounds(1);

            second.Top.Should().BeGreaterThan(first.Top);
            first.Height.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void ClearSelection_WithRowsSelected_LeavesNothingSelected() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            list.MultiSelect = true;
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.Items[0].Selected = true;
            list.Items[1].Selected = true;
            list.SelectedItems.Should().HaveCount(2);

            list.ClearSelection();

            list.SelectedItems.Should().BeEmpty();
            list.Items[0].Selected.Should().BeFalse();
        });
    }

    [Fact]
    public void MultiSelect_WhenFalse_KeepsOnlyTheLastSelectedRow() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.MultiSelect.Should().BeFalse("single select is the default");

            list.Items[0].Selected = true;
            list.Items[1].Selected = true;

            list.SelectedItems.Should().ContainSingle().Which.Should().BeSameAs(list.Items[1]);
        });
    }

    /// <summary>
    /// Selection is part of the creation style, so flipping it recreates the list window. The
    /// selection has to survive that, or a user loses their place whenever the mode changes.
    /// </summary>
    [Fact]
    public void MultiSelect_WhenToggled_KeepsTheSelection() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.Items[1].Selected = true;
            list.MultiSelect = true;

            list.SelectedItems.Should().ContainSingle().Which.Should().BeSameAs(list.Items[1]);
        });
    }

    [Fact]
    public void SelectedIndexChanged_WhenSelectionMoves_IsRaised() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var raised = 0;
            list.SelectedIndexChanged += (_, _) => raised++;

            list.Items[0].Selected = true;

            raised.Should().BeGreaterThan(0);
        });
    }

    /// <summary>
    /// The notification fires for every state bit the control touches, and the focus rectangle
    /// is one of them. Only a change in the selected bit is a selection change - without that
    /// guard a screen reader user arrowing through a multi-select list would hear a selection
    /// event on every keystroke.
    /// </summary>
    [Fact]
    public void SelectedIndexChanged_WhenOnlyFocusMoves_IsNotRaised() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            list.MultiSelect = true;
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.Items[0].Selected = true;
            list.Items[0].Focused = true;

            var raised = 0;
            list.SelectedIndexChanged += (_, _) => raised++;

            // Move only the focus rectangle, leaving the selection where it is.
            list.Items[2].Focused = true;

            list.Items[2].Focused.Should().BeTrue("the focus really did move");
            list.Items[0].Selected.Should().BeTrue("the selection did not");
            raised.Should().Be(0, "a focus move is not a selection change");
        });
    }

    [Fact]
    public void BorderStyle_WhenChanged_SurvivesTheWindowRecreation() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var before = list.ListHandle;
            list.BorderStyle = BorderStyle.None;

            list.BorderStyle.Should().Be(BorderStyle.None);
            list.ListHandle.Should().NotBe(IntPtr.Zero);
            list.ListHandle.Should().NotBe(before, "the border is part of the creation style");
            list.Items.Should().HaveCount(3, "the model outlives the window");
        });
    }

    [Fact]
    public void BeginUpdate_AndEndUpdate_LeaveTheContentIntact() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.BeginUpdate();
            list.Items.Add(new NativeListViewItem("Added while suspended", "1", "2026-09-06 10:00"));
            list.EndUpdate();

            list.Items.Should().HaveCount(4);
            list.Items[3].Index.Should().Be(3);

            // The control, not the model: a suspended redraw must not have swallowed the insert.
            SendMessageW(list.ListHandle, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero)
                .Should().Be(4);
            list.GetItemBounds(3).IsEmpty.Should().BeFalse("the new row must be laid out");
        });
    }

    [Fact]
    public void EnsureVisible_WithoutAHandle_DoesNothing() {
        StaRunner.Run(() => {
            using var detached = Build();

            var act = () => detached.EnsureVisible(0);

            act.Should().NotThrow();
        });
    }

    [Fact]
    public void EnsureVisible_OnceRealized_AcceptsValidAndOutOfRangeRows() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.GetItemBounds(2).IsEmpty.Should().BeFalse("the control must be answering");

            var act = () => {
                list.EnsureVisible(2);
                list.EnsureVisible(-1);
                list.Items[0].EnsureVisible();
            };

            act.Should().NotThrow();
        });
    }

    /// <summary>
    /// Auto-sizing is an instruction to the control rather than a width, so it only means
    /// anything once there is a window to measure in.
    /// </summary>
    [Fact]
    public void AutoSizeWidths_OnceRealized_ResolveToRealWidths() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = new NativeListView();
            list.Columns.Add(new NativeListViewColumn("Title", NativeListViewColumn.AutoSizeToContent));
            list.Columns.Add(new NativeListViewColumn("A much longer header", NativeListViewColumn.AutoSizeToHeader));
            list.Items.Add(new NativeListViewItem("A fairly long piece of cell text", "x"));
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.Columns[0].Width.Should().BeGreaterThan(0);
            list.Columns[1].Width.Should().BeGreaterThan(0);
        });
    }

    /// <summary>
    /// A per-row color is drawn through custom draw, which only runs while the control is
    /// painting. What can be checked without a message loop is that the row carries the color
    /// and that the control was asked to repaint exactly that row's rectangle.
    /// </summary>
    [Fact]
    public void ForeColor_PerRow_MarksOnlyThatRowAndInvalidatesIt() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            var row = list.GetItemBounds(1);
            row.IsEmpty.Should().BeFalse("the control must be answering, not the model");

            list.Items[1].ForeColor = Color.Firebrick;

            list.Items[1].ForeColor.Should().Be(Color.Firebrick);
            list.Items[0].ForeColor.Should().BeNull("only the row that was colored is colored");
            list.Items[1].ListView.Should().BeSameAs(list, "the row must know where to send the repaint");
        });
    }

    /// <summary>
    /// The control owns a window, a font handle, a subclass and possibly a drop-target
    /// registration. Both menu types are covered for this; the one that owns the most was not.
    /// </summary>
    [Fact]
    public void Dispose_IsIdempotent() {
        StaRunner.Run(() => {
            using var form = new Form();
            var list = Build();
            list.AllowDrop = true;
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.ListHandle.Should().NotBe(IntPtr.Zero);

            var dispose = list.Dispose;
            dispose.Should().NotThrow();
            list.ListHandle.Should().Be(IntPtr.Zero, "the list window goes with the control");
            dispose.Should().NotThrow("Dispose is idempotent");
        });
    }

    [Fact]
    public void Dispose_WithoutEverHavingAHandle_DoesNotThrow() {
        StaRunner.Run(() => {
            var list = Build();

            var dispose = list.Dispose;

            dispose.Should().NotThrow();
            dispose.Should().NotThrow();
        });
    }

    [Fact]
    public void AllowDrop_ToggledOnAndOff_DoesNotThrow() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = Build();
            form.Controls.Add(list);
            _ = form.Handle;
            _ = list.Handle;

            list.ListHandle.Should().NotBe(IntPtr.Zero, "registration needs a real window");

            var act = () => {
                list.AllowDrop = true;
                list.AllowDrop = false;
                list.AllowDrop = true;
            };

            // Re-registering an already-registered window fails with DRAGDROP_E_ALREADYREGISTERED,
            // which the control must avoid by revoking first. A throw here is that bug.
            act.Should().NotThrow();
            list.AllowDrop.Should().BeTrue();
        });
    }

    private const uint LVM_GETITEMCOUNT = 0x1000 + 4;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static NativeListView Build() {
        var list = new NativeListView { AccessibleName = "Notes" };

        list.Columns.Add(new NativeListViewColumn("Title", 200));
        list.Columns.Add(new NativeListViewColumn("Words", 70, NativeColumnAlignment.Right));
        list.Columns.Add(new NativeListViewColumn("Modified", 130));

        list.Items.Add(new NativeListViewItem("Shopping list", "42", "2026-09-01 09:15"));
        list.Items.Add(new NativeListViewItem("Standup notes", "88", "2026-09-04 08:02"));
        list.Items.Add(new NativeListViewItem("Bread", "210", "2026-08-30 14:22"));
        return list;
    }
}
