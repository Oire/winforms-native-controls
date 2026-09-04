using Oire.WinForms.NativeControls;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// The validator is the single gate both <see cref="NativeMenuBar"/> and
/// <see cref="NativeContextMenu"/> run a spec through before allocating any HMENU.
/// </summary>
public class MenuSpecValidatorTests {
    private static void NoOp() { }

    [Fact]
    public void Validate_DistinctMnemonics_Passes() {
        var spec = new NativeMenuSpec()
            .AddMenu("&File", file => file.Add("&New", NoOp).Add("E&xit", NoOp))
            .AddMenu("&Edit", edit => edit.Add("Cu&t", NoOp).Add("&Copy", NoOp));

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_TopLevelCollision_Throws() {
        var spec = new NativeMenuSpec()
            .AddMenu("&File", file => file.Add("&New", NoOp))
            .AddMenu("&Format", format => format.Add("&Bold", NoOp));

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'F'*&File*&Format*");
    }

    [Fact]
    public void Validate_SubmenuCollision_Throws() {
        var spec = new NativeMenuSpec()
            .AddMenu("&File", file => file.Add("&New", NoOp).Add("&New Category", NoOp));

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().Throw<InvalidOperationException>().WithMessage("*submenu 'File'*");
    }

    [Fact]
    public void Validate_SameMnemonicInDifferentSubmenus_Passes() {
        var spec = new NativeMenuSpec()
            .AddMenu("&File", file => file.Add("&New", NoOp))
            .AddMenu("&Edit", edit => edit.Add("&New", NoOp));

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MnemonicCollisionIsCaseInsensitive() {
        var spec = new NativeMenuSpec().Add("&File", NoOp).Add("&file", NoOp);

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_SeparatorsAndUnmarkedItemsAreIgnored() {
        var spec = new NativeMenuSpec()
            .Add("Plain one", NoOp)
            .AddSeparator()
            .Add("Plain two", NoOp)
            .AddSeparator();

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RadioSiblingsInSameGroup_Passes() {
        var spec = new NativeMenuSpec()
            .AddMenu("&View", view => view
                .AddRadio("&Tree", "categoryMode", isChecked: true, NoOp)
                .AddRadio("&Flat", "categoryMode", isChecked: false, NoOp)
                .AddRadio("&None", "categoryMode", isChecked: false, NoOp));

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RadioGroupWithNoneChecked_Passes() {
        var spec = new NativeMenuSpec()
            .AddRadio("&Tree", "categoryMode", isChecked: false, NoOp)
            .AddRadio("&Flat", "categoryMode", isChecked: false, NoOp);

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RadioGroupSpanningTwoMenus_Throws() {
        var spec = new NativeMenuSpec()
            .AddMenu("&View", view => view.AddRadio("&Tree", "categoryMode", isChecked: true, NoOp))
            .AddMenu("&Note", note => note.AddRadio("&Flat", "categoryMode", isChecked: false, NoOp));

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().Throw<InvalidOperationException>().WithMessage("*spans more than one menu*");
    }

    [Fact]
    public void Validate_RadioGroupNestedUnderOwnParent_Throws() {
        var spec = new NativeMenuSpec()
            .AddRadio("&Tree", "categoryMode", isChecked: true, NoOp)
            .AddMenu("&More", more => more.AddRadio("&Flat", "categoryMode", isChecked: false, NoOp));

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().Throw<InvalidOperationException>().WithMessage("*spans more than one menu*");
    }

    [Fact]
    public void Validate_TwoRadioItemsInitiallyChecked_Throws() {
        var spec = new NativeMenuSpec()
            .AddRadio("&Tree", "categoryMode", isChecked: true, NoOp)
            .AddRadio("&Flat", "categoryMode", isChecked: true, NoOp);

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().Throw<InvalidOperationException>().WithMessage("*at most one may be checked*");
    }

    [Fact]
    public void Validate_TwoIndependentGroupsInSameMenu_Passes() {
        var spec = new NativeMenuSpec()
            .AddRadio("&Tree", "categoryMode", isChecked: true, NoOp)
            .AddRadio("&Flat", "categoryMode", isChecked: false, NoOp)
            .AddSeparator()
            .AddRadio("&Ascending", "sortOrder", isChecked: true, NoOp)
            .AddRadio("&Descending", "sortOrder", isChecked: false, NoOp);

        var act = () => MenuSpecValidator.Validate(spec);

        act.Should().NotThrow();
    }
}
