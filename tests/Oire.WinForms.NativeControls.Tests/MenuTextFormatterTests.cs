using Oire.WinForms.NativeControls;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// Mnemonic and accelerator-text handling. The <c>&amp;&amp;</c> escape cases matter because
/// localized menu text can legitimately contain a literal ampersand.
/// </summary>
public class MenuTextFormatterTests {
    [Theory]
    [InlineData("&New Note", "Ctrl+N", "&New Note\tCtrl+N")]
    [InlineData("&Save", "Ctrl+S", "&Save\tCtrl+S")]
    [InlineData("&Columns...", null, "&Columns...")]
    [InlineData("&Columns...", "", "&Columns...")]
    public void FormatForWin32_JoinsWithTab(string text, string? shortcut, string expected) {
        MenuTextFormatter.FormatForWin32(text, shortcut).Should().Be(expected);
    }

    [Theory]
    [InlineData("&New Note", "New Note")]
    [InlineData("Cu&t", "Cut")]
    [InlineData("No mnemonic here", "No mnemonic here")]
    [InlineData("&&File", "&File")]
    [InlineData("F&&&ile", "F&ile")]
    [InlineData("Save && Close", "Save & Close")]
    [InlineData("Trailing&", "Trailing")]
    public void StripMnemonic_RemovesMarkerAndUnescapesAmpersands(string text, string expected) {
        MenuTextFormatter.StripMnemonic(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("&New Note", 'N')]
    [InlineData("Cu&t", 'T')]
    [InlineData("E&xit", 'X')]
    [InlineData("F&&&ile", 'I')]
    [InlineData("&file", 'F')]
    public void ExtractMnemonic_ReturnsUppercasedMarkedChar(string text, char expected) {
        MenuTextFormatter.ExtractMnemonic(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("No mnemonic here")]
    [InlineData("&&File")]
    [InlineData("Trailing&")]
    [InlineData("")]
    public void ExtractMnemonic_ReturnsNullWhenNoUnescapedMarker(string text) {
        MenuTextFormatter.ExtractMnemonic(text).Should().BeNull();
    }

    [Fact]
    public void ExtractMnemonic_FirstUnescapedMarkerWins() {
        MenuTextFormatter.ExtractMnemonic("&One &Two").Should().Be('O');
    }

    [Fact]
    public void ExtractMnemonic_MnemonicAtEndOfString() {
        MenuTextFormatter.ExtractMnemonic("Undo &Z").Should().Be('Z');
    }
}
