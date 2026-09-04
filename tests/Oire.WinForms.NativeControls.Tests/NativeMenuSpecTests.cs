using System.Windows.Forms;
using Oire.WinForms.NativeControls;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// Covers the fluent builder in <see cref="NativeMenuSpec"/>. Pure data structure — no
/// interop, no HMENU, so everything here runs headless.
/// </summary>
public class NativeMenuSpecTests {
    private static void NoOp() { }

    [Fact]
    public void Add_ProducesLeafItemWithCallbackAndShortcut() {
        var spec = new NativeMenuSpec().Add("&New Note", "Ctrl+N", Keys.Control | Keys.N, NoOp);

        spec.Items.Should().ContainSingle();
        var item = spec.Items[0];
        item.Text.Should().Be("&New Note");
        item.Shortcut.Should().Be("Ctrl+N");
        item.ShortcutKeys.Should().Be(Keys.Control | Keys.N);
        item.OnClick.Should().NotBeNull();
        item.Children.Should().BeNull();
        item.IsSubMenu.Should().BeFalse();
        item.IsSeparator.Should().BeFalse();
        item.IsCheckable.Should().BeFalse();
        item.RadioGroup.Should().BeNull();
        item.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Add_DisplayOnlyShortcut_KeepsTextButNoKeys() {
        var spec = new NativeMenuSpec().Add("Cu&t", "Ctrl+X", shortcutKeys: null, NoOp);

        spec.Items[0].Shortcut.Should().Be("Ctrl+X");
        spec.Items[0].ShortcutKeys.Should().BeNull();
    }

    [Fact]
    public void AddMenu_NestsChildrenInDeclarationOrder() {
        var spec = new NativeMenuSpec()
            .AddMenu("&File", file => file
                .Add("&New", NoOp)
                .AddSeparator()
                .Add("E&xit", NoOp))
            .AddMenu("&Help", help => help.Add("&About", NoOp));

        spec.Items.Should().HaveCount(2);
        spec.Items[0].Text.Should().Be("&File");
        spec.Items[0].IsSubMenu.Should().BeTrue();
        spec.Items[0].OnClick.Should().BeNull();

        var file = spec.Items[0].Children!;
        file.Should().HaveCount(3);
        file[0].Text.Should().Be("&New");
        file[1].IsSeparator.Should().BeTrue();
        file[2].Text.Should().Be("E&xit");

        spec.Items[1].Children.Should().ContainSingle();
    }

    [Fact]
    public void AddMenu_NestedSubmenusTraverseInOrder() {
        var spec = new NativeMenuSpec()
            .AddMenu("&View", view => view
                .Add("&Columns...", NoOp)
                .AddMenu("&Sort By", sort => sort
                    .Add("&Title", NoOp)
                    .Add("&Created", NoOp)));

        var view = spec.Items[0].Children!;
        view[1].Text.Should().Be("&Sort By");
        view[1].IsSubMenu.Should().BeTrue();
        view[1].Children!.Select(i => i.Text).Should().Equal("&Title", "&Created");
    }

    [Fact]
    public void AddSeparator_LandsAtDeclaredPosition() {
        var spec = new NativeMenuSpec()
            .Add("One", NoOp)
            .AddSeparator()
            .Add("Two", NoOp)
            .AddSeparator();

        spec.Items.Select(i => i.IsSeparator).Should().Equal(false, true, false, true);
        spec.Items[1].OnClick.Should().BeNull();
        spec.Items[1].Text.Should().BeEmpty();
    }

    [Fact]
    public void AddCheckable_RoundTripsCheckedState() {
        var spec = new NativeMenuSpec()
            .AddCheckable("Show &Preview", isChecked: true, NoOp)
            .AddCheckable("Show &Deleted Items", isChecked: false, NoOp);

        spec.Items[0].IsCheckable.Should().BeTrue();
        spec.Items[0].IsChecked.Should().BeTrue();
        spec.Items[0].RadioGroup.Should().BeNull();
        spec.Items[1].IsChecked.Should().BeFalse();
    }

    [Fact]
    public void IsChecked_IsMutableAfterBuild() {
        var spec = new NativeMenuSpec().AddCheckable("Toggle", isChecked: false, NoOp);

        spec.Items[0].IsChecked = true;

        spec.Items[0].IsChecked.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_IsMutableAfterBuild() {
        var spec = new NativeMenuSpec().Add("Save", NoOp);

        spec.Items[0].IsEnabled = false;

        spec.Items[0].IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void AddRadio_RoundTripsGroupAndCheckedState() {
        var spec = new NativeMenuSpec()
            .AddRadio("&Tree", "categoryMode", isChecked: true, NoOp)
            .AddRadio("&Flat", "categoryMode", isChecked: false, NoOp)
            .AddRadio("&None", "categoryMode", isChecked: false, NoOp);

        spec.Items.Should().AllSatisfy(i => {
            i.RadioGroup.Should().Be("categoryMode");
            i.IsCheckable.Should().BeTrue();
        });
        spec.Items.Select(i => i.IsChecked).Should().Equal(true, false, false);
    }

    [Fact]
    public void AddRadio_SettingOneChecked_DoesNotClearSiblings() {
        // Mutual exclusion is NativeMenuBar's job at click time, not the spec's.
        var spec = new NativeMenuSpec()
            .AddRadio("&Tree", "categoryMode", isChecked: true, NoOp)
            .AddRadio("&Flat", "categoryMode", isChecked: false, NoOp);

        spec.Items[1].IsChecked = true;

        spec.Items[0].IsChecked.Should().BeTrue();
        spec.Items[1].IsChecked.Should().BeTrue();
    }

    [Fact]
    public void AddRadio_PreservesShortcutAndKeys() {
        var spec = new NativeMenuSpec()
            .AddRadio("&Tree", "categoryMode", isChecked: false, "Ctrl+1", Keys.Control | Keys.D1, NoOp);

        spec.Items[0].Shortcut.Should().Be("Ctrl+1");
        spec.Items[0].ShortcutKeys.Should().Be(Keys.Control | Keys.D1);
    }

    [Fact]
    public void AddRadio_BlankGroup_Throws() {
        var act = () => new NativeMenuSpec().AddRadio("&Tree", "  ", isChecked: false, NoOp);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_NullCallback_Throws() {
        var act = () => new NativeMenuSpec().Add("Broken", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
