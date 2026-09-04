using System.Windows.Forms;
using Oire.WinForms.NativeControls;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// The <see cref="Keys"/> to <c>ACCEL</c> mapping. Values are checked against the Win32
/// <c>fVirt</c> bits: FVIRTKEY 0x01, FSHIFT 0x04, FCONTROL 0x08, FALT 0x10.
/// </summary>
public class AccelConverterTests {
    private const byte FVirtKey = 0x01;
    private const byte FShift = 0x04;
    private const byte FControl = 0x08;
    private const byte FAlt = 0x10;

    [Fact]
    public void ConvertKey_PlainFunctionKey_SetsOnlyVirtKeyFlag() {
        var (fVirt, key) = AccelConverter.ConvertKey(Keys.F1);

        fVirt.Should().Be(FVirtKey);
        key.Should().Be((ushort)Keys.F1);
    }

    [Fact]
    public void ConvertKey_Control_SetsControlFlag() {
        var (fVirt, key) = AccelConverter.ConvertKey(Keys.Control | Keys.N);

        fVirt.Should().Be(FVirtKey | FControl);
        key.Should().Be((ushort)Keys.N);
    }

    [Fact]
    public void ConvertKey_ControlShift_SetsBothModifierFlags() {
        var (fVirt, key) = AccelConverter.ConvertKey(Keys.Control | Keys.Shift | Keys.N);

        fVirt.Should().Be(FVirtKey | FControl | FShift);
        key.Should().Be((ushort)Keys.N);
    }

    [Fact]
    public void ConvertKey_AltF4_SetsAltFlag() {
        var (fVirt, key) = AccelConverter.ConvertKey(Keys.Alt | Keys.F4);

        fVirt.Should().Be(FVirtKey | FAlt);
        key.Should().Be((ushort)Keys.F4);
    }

    [Fact]
    public void ConvertKey_AllThreeModifiers_SetsAllFlags() {
        var (fVirt, _) = AccelConverter.ConvertKey(Keys.Control | Keys.Shift | Keys.Alt | Keys.Up);

        fVirt.Should().Be(FVirtKey | FControl | FShift | FAlt);
    }

    [Fact]
    public void ConvertKey_StripsModifiersFromKeyCode() {
        var (_, key) = AccelConverter.ConvertKey(Keys.Control | Keys.Shift | Keys.Oemcomma);

        key.Should().Be((ushort)Keys.Oemcomma);
    }

    [Fact]
    public void ConvertKey_None_Throws() {
        var act = () => AccelConverter.ConvertKey(Keys.None);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConvertKey_ModifiersWithoutKeyCode_Throws() {
        var act = () => AccelConverter.ConvertKey(Keys.Control | Keys.Shift);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(Keys.F1)]
    [InlineData(Keys.Delete)]
    [InlineData(Keys.Control | Keys.S)]
    [InlineData(Keys.Shift | Keys.F1)]
    public void ConvertKey_AlwaysSetsVirtKeyFlag(Keys keys) {
        var (fVirt, _) = AccelConverter.ConvertKey(keys);

        (fVirt & FVirtKey).Should().Be(FVirtKey);
    }
}
