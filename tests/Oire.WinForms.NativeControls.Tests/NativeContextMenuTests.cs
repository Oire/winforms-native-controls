using System.Drawing;
using System.Windows.Forms;
using Oire.WinForms.NativeControls;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// Popup-menu construction, id allocation, and rebuild. Actually tracking a popup would block
/// on a nested Win32 message loop, so the display path is covered by the manual JAWS checklist
/// in the plan instead.
/// </summary>
public class NativeContextMenuTests {
    [Fact]
    public void Constructor_AssignsOneIdPerLeaf() {
        StaRunner.Run(() => {
            using var menu = new NativeContextMenu(new NativeMenuSpec()
                .Add("&Open", NoOp)
                .Add("&Delete", NoOp)
                .AddSeparator()
                .Add("E&xport...", NoOp));

            menu.CommandIds.Should().HaveCount(3);
            menu.CommandIds.Should().OnlyHaveUniqueItems();
            menu.CommandIds.Should().AllSatisfy(id => id.Should().BeInRange(0x1000, 0xDFFF));
        });
    }

    [Fact]
    public void TryRoute_ReturnsEachItemCallback() {
        StaRunner.Run(() => {
            var fired = new List<string>();
            using var menu = new NativeContextMenu(new NativeMenuSpec()
                .Add("&Open", () => fired.Add("open"))
                .Add("&Delete", () => fired.Add("delete")));

            foreach (var id in menu.CommandIds) {
                menu.TryRoute(id, out var callback).Should().BeTrue();
                callback!();
            }

            fired.Should().BeEquivalentTo(["open", "delete"]);
        });
    }

    [Fact]
    public void TryRoute_UnknownId_ReturnsFalse() {
        StaRunner.Run(() => {
            using var menu = new NativeContextMenu(new NativeMenuSpec().Add("&Open", NoOp));

            menu.TryRoute(0x0777, out var callback).Should().BeFalse();
            callback.Should().BeNull();
        });
    }

    [Fact]
    public void Rebuild_ProducesFreshRouting() {
        StaRunner.Run(() => {
            var fired = new List<string>();
            using var menu = new NativeContextMenu(new NativeMenuSpec()
                .Add("&Open", () => fired.Add("old-open"))
                .Add("&Delete", () => fired.Add("old-delete"))
                .Add("E&xport...", () => fired.Add("old-export")));

            var surplusId = menu.CommandIds.Max();

            menu.Rebuild(new NativeMenuSpec().Add("&Offnen", () => fired.Add("new-offnen")));

            menu.CommandIds.Should().ContainSingle();
            menu.TryRoute(surplusId, out _).Should().BeFalse();

            menu.TryRoute(menu.CommandIds.Single(), out var callback).Should().BeTrue();
            callback!();
            fired.Should().BeEquivalentTo(["new-offnen"]);
        });
    }

    [Fact]
    public void Constructor_MnemonicCollision_Throws() {
        StaRunner.Run(() => {
            var act = () => new NativeContextMenu(new NativeMenuSpec()
                .Add("&Open", NoOp)
                .Add("&Order", NoOp));

            act.Should().Throw<InvalidOperationException>().WithMessage("*Mnemonic 'O'*");
        });
    }

    [Fact]
    public void Constructor_RadioGroupViolation_Throws() {
        StaRunner.Run(() => {
            var act = () => new NativeContextMenu(new NativeMenuSpec()
                .AddRadio("&Tree", "mode", isChecked: true, NoOp)
                .AddRadio("&Flat", "mode", isChecked: true, NoOp));

            act.Should().Throw<InvalidOperationException>().WithMessage("*at most one may be checked*");
        });
    }

    [Fact]
    public void Rebuild_WhileTrackingAPopup_Throws() {
        StaRunner.Run(() => {
            using var menu = new NativeContextMenu(new NativeMenuSpec().Add("&Open", NoOp));

            using (MenuTrackingScope.Enter()) {
                var act = () => menu.Rebuild(new NativeMenuSpec().Add("&Open", NoOp));

                act.Should().Throw<InvalidOperationException>().WithMessage("*popup menu is open*");
            }

            var after = () => menu.Rebuild(new NativeMenuSpec().Add("&Open", NoOp));
            after.Should().NotThrow();
        });
    }

    [Fact]
    public void AttachTo_Twice_Throws() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var first = new ListBox();
            using var second = new ListBox();
            form.Controls.Add(first);
            form.Controls.Add(second);

            using var menu = new NativeContextMenu(new NativeMenuSpec().Add("&Open", NoOp));
            menu.AttachTo(first);

            var act = () => menu.AttachTo(second);

            act.Should().Throw<InvalidOperationException>().WithMessage("*one instance per control*");
        });
    }

    [Fact]
    public void Dispose_IsIdempotent() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = new ListBox();
            form.Controls.Add(list);

            var menu = new NativeContextMenu(new NativeMenuSpec().Add("&Open", NoOp));
            menu.AttachTo(list);
            menu.Dispose();

            var again = menu.Dispose;
            again.Should().NotThrow();
        });
    }

    [Fact]
    public void Show_AfterDispose_Throws() {
        StaRunner.Run(() => {
            using var form = new Form();
            var menu = new NativeContextMenu(new NativeMenuSpec().Add("&Open", NoOp));
            menu.Dispose();

            var act = () => menu.Show(form, Point.Empty);

            act.Should().Throw<ObjectDisposedException>();
        });
    }

    [Fact]
    public void Resolver_DivertsToAnotherMenu() {
        StaRunner.Run(() => {
            using var body = new NativeContextMenu(new NativeMenuSpec().Add("&Open", NoOp));
            using var header = new NativeContextMenu(new NativeMenuSpec().Add("&Columns...", NoOp));

            NativeContextMenu? chosen = null;
            body.Resolver = request => {
                chosen = request.FromKeyboard ? body : header;
                // Returning null keeps the test off the blocking TrackPopupMenuEx path while
                // still proving the resolver saw the request.
                return null;
            };

            using var form = new Form();
            using var list = new ListView();
            form.Controls.Add(list);
            body.AttachTo(list);

            body.HandleContextMenu(new IntPtr(-1)).Should().BeTrue();
            chosen.Should().BeSameAs(body);

            body.HandleContextMenu(MakeAnchor(120, 40)).Should().BeTrue();
            chosen.Should().BeSameAs(header);
        });
    }

    [Fact]
    public void HandleContextMenu_DecodesSignedScreenCoordinates() {
        StaRunner.Run(() => {
            using var menu = new NativeContextMenu(new NativeMenuSpec().Add("&Open", NoOp));
            Point? seen = null;
            var fromKeyboard = true;
            menu.Resolver = request => {
                seen = request.ScreenLocation;
                fromKeyboard = request.FromKeyboard;
                return null;
            };

            using var form = new Form();
            using var list = new ListBox();
            form.Controls.Add(list);
            menu.AttachTo(list);

            // Negative coordinates are legitimate on a multi-monitor desktop.
            menu.HandleContextMenu(MakeAnchor(-1200, -50)).Should().BeTrue();

            seen.Should().Be(new Point(-1200, -50));
            fromKeyboard.Should().BeFalse();
        });
    }

    private static IntPtr MakeAnchor(int x, int y) =>
        new((ushort)x | ((long)(ushort)y << 16));

    private static void NoOp() { }
}
