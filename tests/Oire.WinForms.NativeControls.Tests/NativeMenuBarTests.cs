using System.Windows.Forms;
using Oire.WinForms.NativeControls;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// Exercises <see cref="NativeMenuBar"/> against a real form handle and real <c>HMENU</c>
/// allocation, on an STA thread. Routing is verified through the internal <c>TryRoute</c>
/// seam rather than by driving Windows' menu UI.
/// </summary>
public class NativeMenuBarTests {
    [Fact]
    public void Attach_AssignsOneIdPerLeaf() {
        StaRunner.Run(() => {
            var spec = new NativeMenuSpec()
                .AddMenu("&File", file => file
                    .Add("&New", NoOp)
                    .AddSeparator()
                    .Add("E&xit", NoOp))
                .AddMenu("&Help", help => help.Add("&About", NoOp));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(spec);

            // Three leaves; the two submenu containers and the separator consume no id.
            bar.CommandIds.Should().HaveCount(3);
            bar.CommandIds.Should().OnlyHaveUniqueItems();
            bar.CommandIds.Should().AllSatisfy(id => id.Should().BeInRange(0x1000, 0xDFFF));
        });
    }

    [Fact]
    public void TryRoute_ReturnsTheCallbackForEachLeaf() {
        StaRunner.Run(() => {
            var fired = new List<string>();
            var spec = new NativeMenuSpec()
                .AddMenu("&File", file => file
                    .Add("&New", () => fired.Add("new"))
                    .Add("E&xit", () => fired.Add("exit")));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(spec);

            foreach (var id in bar.CommandIds) {
                bar.TryRoute(id, out var callback).Should().BeTrue();
                callback!();
            }

            fired.Should().BeEquivalentTo(["new", "exit"]);
        });
    }

    [Fact]
    public void TryRoute_UnknownId_ReturnsFalse() {
        StaRunner.Run(() => {
            var spec = new NativeMenuSpec().AddMenu("&File", file => file.Add("&New", NoOp));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(spec);

            bar.TryRoute(0x0999, out var callback).Should().BeFalse();
            callback.Should().BeNull();
        });
    }

    [Fact]
    public void Rebuild_InvalidatesOldIdsAndRoutesNewOnes() {
        StaRunner.Run(() => {
            var fired = new List<string>();
            var first = new NativeMenuSpec()
                .AddMenu("&File", file => file
                    .Add("&New", () => fired.Add("old-new"))
                    .Add("E&xit", () => fired.Add("old-exit")));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(first);
            var oldIds = bar.CommandIds.ToArray();

            var second = new NativeMenuSpec()
                .AddMenu("&Datei", datei => datei
                    .Add("&Neu", () => fired.Add("new-neu"))
                    .Add("&Beenden", () => fired.Add("new-beenden"))
                    .Add("&Speichern", () => fired.Add("new-speichern")));
            bar.Rebuild(second);

            var newIds = bar.CommandIds.ToArray();
            newIds.Should().HaveCount(3);

            // Ids restart at the base of the range on every build, so the third id is new.
            foreach (var id in oldIds.Except(newIds)) {
                bar.TryRoute(id, out _).Should().BeFalse();
            }

            foreach (var id in newIds) {
                bar.TryRoute(id, out var callback).Should().BeTrue();
                callback!();
            }

            fired.Should().BeEquivalentTo(["new-neu", "new-beenden", "new-speichern"]);
        });
    }

    [Fact]
    public void Rebuild_ShrinkingMenu_DropsTheSurplusIds() {
        StaRunner.Run(() => {
            var big = new NativeMenuSpec()
                .AddMenu("&File", file => file.Add("&New", NoOp).Add("&Open", NoOp).Add("E&xit", NoOp));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(big);
            var surplusId = bar.CommandIds.Max();

            bar.Rebuild(new NativeMenuSpec().AddMenu("&File", file => file.Add("&New", NoOp)));

            bar.CommandIds.Should().ContainSingle();
            bar.TryRoute(surplusId, out _).Should().BeFalse();
        });
    }

    [Fact]
    public void Attach_MnemonicCollision_Throws() {
        StaRunner.Run(() => {
            var spec = new NativeMenuSpec()
                .AddMenu("&File", file => file.Add("&New", NoOp))
                .AddMenu("&Format", format => format.Add("&Bold", NoOp));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);

            var act = () => bar.Attach(spec);

            act.Should().Throw<InvalidOperationException>().WithMessage("*Mnemonic 'F'*");
        });
    }

    [Fact]
    public void Attach_RadioGroupSpanningMenus_Throws() {
        StaRunner.Run(() => {
            var spec = new NativeMenuSpec()
                .AddMenu("&View", view => view.AddRadio("&Tree", "categoryMode", isChecked: true, NoOp))
                .AddMenu("&Note", note => note.AddRadio("&Flat", "categoryMode", isChecked: false, NoOp));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);

            var act = () => bar.Attach(spec);

            act.Should().Throw<InvalidOperationException>().WithMessage("*spans more than one menu*");
        });
    }

    [Fact]
    public void Attach_TwoCheckedRadiosInOneGroup_Throws() {
        StaRunner.Run(() => {
            var spec = new NativeMenuSpec()
                .AddMenu("&View", view => view
                    .AddRadio("&Tree", "categoryMode", isChecked: true, NoOp)
                    .AddRadio("&Flat", "categoryMode", isChecked: true, NoOp));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);

            var act = () => bar.Attach(spec);

            act.Should().Throw<InvalidOperationException>().WithMessage("*at most one may be checked*");
        });
    }

    [Fact]
    public void Attach_FailedValidation_LeavesTheBarDisposable() {
        StaRunner.Run(() => {
            var spec = new NativeMenuSpec().Add("&File", NoOp).Add("&Find", NoOp);

            using var form = new Form();
            var bar = new NativeMenuBar(form);

            var attach = () => bar.Attach(spec);
            attach.Should().Throw<InvalidOperationException>();

            // Nothing was registered, so teardown must be a silent no-op rather than a throw.
            var dispose = bar.Dispose;
            dispose.Should().NotThrow();
            dispose.Should().NotThrow("Dispose is idempotent");
        });
    }

    [Fact]
    public void TryDispatch_RadioItem_ChecksClickedAndClearsSiblings() {
        StaRunner.Run(() => {
            var spec = new NativeMenuSpec()
                .AddMenu("&View", view => view
                    .AddRadio("&Tree", "categoryMode", isChecked: true, NoOp)
                    .AddRadio("&Flat", "categoryMode", isChecked: false, NoOp)
                    .AddRadio("&None", "categoryMode", isChecked: false, NoOp));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(spec);

            var items = spec.Items[0].Children!;
            var flatId = bar.CommandIds.ElementAt(1);

            bar.TryDispatch(flatId).Should().BeTrue();

            items.Select(i => i.IsChecked).Should().Equal(false, true, false);
        });
    }

    [Fact]
    public void TryDispatch_DisabledItem_ConsumesTheIdWithoutRunningTheCallback() {
        StaRunner.Run(() => {
            var fired = false;
            var spec = new NativeMenuSpec()
                .AddMenu("&File", file => file.Add("&Save", () => fired = true));

            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(spec);

            spec.Items[0].Children![0].IsEnabled = false;
            var id = bar.CommandIds.Single();

            // True because the id is ours; the callback stays unrun because the item is grayed.
            bar.TryDispatch(id).Should().BeTrue();
            fired.Should().BeFalse();
        });
    }

    [Fact]
    public void TryDispatch_UnknownId_ReturnsFalse() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(new NativeMenuSpec().AddMenu("&File", file => file.Add("&New", NoOp)));

            bar.TryDispatch(0x0042).Should().BeFalse();
        });
    }

    [Fact]
    public void Rebuild_WhileTrackingAPopup_Throws() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(new NativeMenuSpec().AddMenu("&File", file => file.Add("&New", NoOp)));

            using (MenuTrackingScope.Enter()) {
                var act = () => bar.Rebuild(new NativeMenuSpec().AddMenu("&File", file => file.Add("&New", NoOp)));

                act.Should().Throw<InvalidOperationException>().WithMessage("*popup menu is open*");
            }

            // Once tracking ends the rebuild goes through.
            var after = () => bar.Rebuild(new NativeMenuSpec().AddMenu("&File", file => file.Add("&New", NoOp)));
            after.Should().NotThrow();
        });
    }

    [Fact]
    public void Dispose_IsIdempotent() {
        StaRunner.Run(() => {
            using var form = new Form();
            var bar = new NativeMenuBar(form);
            bar.Attach(new NativeMenuSpec().AddMenu("&File", file => file.Add("&New", NoOp)));

            bar.Dispose();

            var again = bar.Dispose;
            again.Should().NotThrow();
        });
    }

    [Fact]
    public void Attach_AfterDispose_Throws() {
        StaRunner.Run(() => {
            using var form = new Form();
            var bar = new NativeMenuBar(form);
            bar.Dispose();

            var act = () => bar.Attach(new NativeMenuSpec().Add("&New", NoOp));

            act.Should().Throw<ObjectDisposedException>();
        });
    }

    private static void NoOp() { }
}
