namespace Oire.WinForms.NativeControls;

/// <summary>
/// Answers whether a screen point lands on a <see cref="ListView"/>'s column-header band.
/// </summary>
/// <remarks>
/// A ListView in Details view carries a separate header window, and a right-click on it should
/// open the column menu rather than the row menu. WinForms exposes no hit-test for the header,
/// so the question goes to the header window itself via <c>HDM_HITTEST</c>.
/// </remarks>
public static class ListViewHeaderHitTest {
    /// <summary>
    /// True when <paramref name="screenLocation"/> falls on the header band — on a column, on
    /// a divider, or in a divider's widened grab area. False in any view other than Details,
    /// where the list has no header window at all.
    /// </summary>
    public static bool IsOnHeader(ListView listView, Point screenLocation) {
        ArgumentNullException.ThrowIfNull(listView);

        if (!listView.IsHandleCreated) {
            return false;
        }

        var header = Win32Interop.SendMessageW(
            listView.Handle, Win32Interop.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        if (header == IntPtr.Zero) {
            return false;
        }

        // HDM_HITTEST wants header-window client coordinates, not the ListView's.
        var point = new Win32Interop.POINT { X = screenLocation.X, Y = screenLocation.Y };
        if (!Win32Interop.ScreenToClient(header, ref point)) {
            return false;
        }

        var info = new Win32Interop.HDHITTESTINFO { Point = point };
        var index = Win32Interop.SendMessageW(header, Win32Interop.HDM_HITTEST, IntPtr.Zero, ref info);
        if (index.ToInt64() < 0) {
            return false;
        }

        // HHT_ONFILTER and HHT_ONFILTERBUTTON are not tested for: this header has no filter bar.
        const uint onHeaderBand =
            Win32Interop.HHT_ONHEADER | Win32Interop.HHT_ONDIVIDER | Win32Interop.HHT_ONDIVOPEN;
        return (info.Flags & onHeaderBand) != 0;
    }
}
