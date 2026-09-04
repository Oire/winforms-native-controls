namespace Oire.WinForms.NativeControls;

/// <summary>
/// Maps a WinForms <see cref="Keys"/> chord onto the <c>fVirt</c> / <c>key</c> pair a Win32
/// <c>ACCEL</c> entry expects.
/// </summary>
/// <remarks>
/// Split out from <see cref="NativeMenuBar"/> so the mapping can be tested on its own rather
/// than only end to end through a real <c>CreateAcceleratorTableW</c> call.
/// </remarks>
public static class AccelConverter {
    /// <summary>
    /// Converts <paramref name="keys"/> into accelerator-table flags and virtual-key code.
    /// <c>FVIRTKEY</c> is always set: menu accelerators are key chords, never raw character
    /// codes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="keys"/> carries no key code (modifiers alone, or <see cref="Keys.None"/>).
    /// </exception>
    public static (byte FVirt, ushort Key) ConvertKey(Keys keys) {
        var keyCode = keys & Keys.KeyCode;
        if (keyCode == Keys.None) {
            throw new ArgumentException("An accelerator needs a key code, not modifiers alone.", nameof(keys));
        }

        var fVirt = Win32Interop.FVIRTKEY;
        if ((keys & Keys.Control) == Keys.Control) {
            fVirt |= Win32Interop.FCONTROL;
        }

        if ((keys & Keys.Shift) == Keys.Shift) {
            fVirt |= Win32Interop.FSHIFT;
        }

        if ((keys & Keys.Alt) == Keys.Alt) {
            fVirt |= Win32Interop.FALT;
        }

        return (fVirt, (ushort)keyCode);
    }
}
