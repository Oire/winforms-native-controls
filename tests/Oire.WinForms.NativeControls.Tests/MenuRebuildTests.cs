using System.Windows.Forms;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// Rebuilding a live menu from a freshly built spec — the path an application takes when its
/// language changes and every label has to be re-evaluated without a restart.
/// </summary>
public class MenuRebuildTests {
    [Fact]
    public void Rebuild_ReplacesLabelsAndCallbacksWithoutChangingShape() {
        StaRunner.Run(() => {
            var invoked = new List<string>();

            // Stands in for an application's spec builder: every label is re-evaluated on each
            // call, which is what makes a language switch take effect without a restart.
            var language = "en";
            NativeMenuSpec BuildSpec() {
                var file = language == "en" ? "&File" : "&Datei";
                var open = language == "en" ? "&Open" : "&Offnen";
                var quit = language == "en" ? "E&xit" : "&Beenden";
                return new NativeMenuSpec().AddMenu(file, menu => menu
                    .Add(open, () => invoked.Add($"open:{language}"))
                    .Add(quit, () => invoked.Add($"quit:{language}")));
            }

            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            using var context = new NativeContextMenu(BuildSpec());
            bar.Attach(BuildSpec());

            var beforeBar = bar.CommandIds.ToArray();
            var beforeContext = context.CommandIds.ToArray();

            language = "de";
            bar.Rebuild(BuildSpec());
            context.Rebuild(BuildSpec());

            // Same shape, so the id count matches; what changed is the callbacks behind them.
            bar.CommandIds.Should().HaveCount(beforeBar.Length);
            context.CommandIds.Should().HaveCount(beforeContext.Length);

            foreach (var id in bar.CommandIds) {
                bar.TryRoute(id, out var callback).Should().BeTrue();
                callback!();
            }

            invoked.Should().BeEquivalentTo(["open:de", "quit:de"]);
        });
    }

    [Fact]
    public void Rebuild_ToAShorterMenu_DropsTheStaleIds() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var bar = new NativeMenuBar(form);
            bar.Attach(new NativeMenuSpec().AddMenu("&File", file => file
                .Add("&Open", NoOp)
                .Add("&Save", NoOp)
                .Add("E&xit", NoOp)));

            var staleId = bar.CommandIds.Max();

            bar.Rebuild(new NativeMenuSpec().AddMenu("&Datei", datei => datei.Add("&Offnen", NoOp)));

            bar.CommandIds.Should().ContainSingle();
            bar.TryRoute(staleId, out _).Should().BeFalse();
        });
    }

    [Fact]
    public void Rebuild_KeepsTheContextMenuAttachedToItsControl() {
        StaRunner.Run(() => {
            using var form = new Form();
            using var list = new ListBox();
            form.Controls.Add(list);

            using var menu = new NativeContextMenu(new NativeMenuSpec().Add("&Open", NoOp));
            menu.AttachTo(list);

            var seen = 0;
            menu.Resolver = _ => { seen++; return null; };

            menu.Rebuild(new NativeMenuSpec().Add("&Offnen", NoOp));

            // The subclass survives a rebuild, so the control still routes WM_CONTEXTMENU here.
            menu.HandleContextMenu(new IntPtr(-1)).Should().BeTrue();
            seen.Should().Be(1);
        });
    }

    /// <summary>
    /// A popup being tracked on one thread must not stop another thread rebuilding its own
    /// menus. A menu belongs to the thread that created it, so the tracking depth does too.
    /// </summary>
    /// <remarks>
    /// This is a regression guard for a real CI failure. The depth used to be a plain static,
    /// and since every test here runs on its own thread and xUnit runs test classes in
    /// parallel, one class holding a tracking scope made an unrelated rebuild in another class
    /// throw "Cannot rebuild the menu bar while a popup menu is open". It failed on master
    /// while passing on the branch, which is what a race looks like.
    /// </remarks>
    [Fact]
    public void Tracking_OnOneThread_DoesNotBlockAnother() {
        using var entered = new ManualResetEventSlim();
        using var mayLeave = new ManualResetEventSlim();

        var trackingThread = new Thread(() => {
            using (MenuTrackingScope.Enter()) {
                MenuTrackingScope.IsTracking.Should().BeTrue("this thread is the one tracking");
                entered.Set();
                mayLeave.Wait(TimeSpan.FromSeconds(10));
            }
        });

        trackingThread.SetApartmentState(ApartmentState.STA);
        trackingThread.IsBackground = true;
        trackingThread.Start();
        entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the other thread must get going");

        try {
            // Same moment, different thread: this one has no menu open and must be free to act.
            MenuTrackingScope.IsTracking.Should().BeFalse("another thread's popup is not ours");

            StaRunner.Run(() => {
                using var form = new Form();
                _ = form.Handle;

                using var bar = new NativeMenuBar(form);
                bar.Attach(new NativeMenuSpec().AddMenu("&File", file => file.Add("&New", NoOp)));

                var rebuild = () => bar.Rebuild(
                    new NativeMenuSpec().AddMenu("&Datei", file => file.Add("&Neu", NoOp)));

                rebuild.Should().NotThrow("no popup is open on this thread");
            });
        } finally {
            mayLeave.Set();
            trackingThread.Join(TimeSpan.FromSeconds(10));
        }
    }

    private static void NoOp() { }
}
