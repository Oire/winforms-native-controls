namespace Oire.WinForms.NativeControls;

/// <summary>
/// The <c>fVirt</c> and <c>key</c> pair of one Win32 <c>ACCEL</c> entry.
/// </summary>
/// <remarks>
/// A named type rather than a tuple: tuple element names are compiler metadata rather than part
/// of the type, so they are lost the moment the value passes through a generic signature, and a
/// public API should not hand callers a pair of values it cannot keep labeled.
/// </remarks>
/// <param name="FVirt">Accelerator flags: <c>FVIRTKEY</c> plus any modifiers.</param>
/// <param name="Key">The virtual-key code.</param>
public readonly record struct AcceleratorEntry(byte FVirt, ushort Key);
