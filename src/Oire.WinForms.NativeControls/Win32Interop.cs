using System.Runtime.InteropServices;

namespace Oire.WinForms.NativeControls;

/// <summary>
/// P/Invoke surface for the native menu wrapper: menu creation and mutation, popup tracking,
/// accelerator tables, and the ListView-header hit-test used to split the note context menu
/// from the column-header context menu.
/// </summary>
/// <remarks>
/// <para>
/// Handles are plain <see cref="IntPtr"/> rather than <c>HandleRef</c>. <c>HandleRef</c> exists
/// to keep a wrapper object alive for the duration of a call; here every handle owner
/// (<see cref="NativeMenuBar"/>, <see cref="NativeContextMenu"/>, the owning <c>Form</c>) is
/// reachable through <c>this</c> or a field across the whole call, so nothing can be collected
/// mid-P/Invoke. That is a property of this ownership model, not a general rule.
/// </para>
/// <para>
/// Every entry point is declared with its explicit <c>W</c> suffix and
/// <c>ExactSpelling = true</c> so the runtime never guesses an ANSI variant.
/// </para>
/// </remarks>
internal static class Win32Interop {
    // --- Menu creation and teardown -----------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr CreateMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(IntPtr hMenu);

    // --- Menu population ----------------------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenuW(
        IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, [MarshalAs(UnmanagedType.LPWStr)] string? lpNewItem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InsertMenuItemW(
        IntPtr hMenu, uint item, [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFOW lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ModifyMenuW(
        IntPtr hMenu, uint uPosition, uint uFlags, UIntPtr uIDNewItem,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpNewItem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMenuItemInfoW(
        IntPtr hMenu, uint item, [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFOW lpmii);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern int GetMenuItemCount(IntPtr hMenu);

    // --- Menu state ---------------------------------------------------------------------

    /// <summary>Returns the previous state, or -1 when the item does not exist.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

    /// <summary>Returns the previous state, or <c>0xFFFFFFFF</c> when the item does not exist.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint CheckMenuItem(IntPtr hMenu, uint uIDCheckItem, uint uCheck);

    // --- Attaching and showing ----------------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMenu(IntPtr hWnd, IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DrawMenuBar(IntPtr hWnd);

    /// <summary>
    /// Shows a popup menu. With <see cref="TPM_RETURNCMD"/> the return value is the chosen
    /// command id (0 when the user dismissed the menu), not a boolean. The final parameter is
    /// an optional <c>LPTPMPARAMS</c>; pass <see cref="IntPtr.Zero"/> for default placement.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern int TrackPopupMenuEx(
        IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    // --- Accelerator tables -------------------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr CreateAcceleratorTableW([In] ACCEL[] paccel, int cAccel);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int TranslateAcceleratorW(IntPtr hWnd, IntPtr hAccTable, ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyAcceleratorTable(IntPtr hAccel);

    // --- Coordinates and hit-testing ----------------------------------------------------

    /// <summary>Screen coordinates of the last message: x in the low word, y in the high word.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint GetMessagePos();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, ref HDHITTESTINFO lParam);

    // --- Accessibility ------------------------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr GetFocus();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Reserves a process-wide unique message id. Safer than <c>WM_APP + n</c> on a window
    /// whose WndProc belongs to WinForms and to the hosting application alike.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern uint RegisterWindowMessageW([MarshalAs(UnmanagedType.LPWStr)] string lpString);

    /// <summary>
    /// Raises a WinEvent for assistive technologies. Used to re-announce the focused control
    /// after a menu closes: leaving menu mode moves no focus, so it fires nothing by itself.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern void NotifyWinEvent(uint eventId, IntPtr hWnd, int idObject, int idChild);

    // --- Structs ------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG {
        internal IntPtr HWnd;
        internal uint Message;
        internal IntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal POINT Point;
    }

    /// <summary>
    /// <c>ACCEL</c> from <c>winuser.h</c>. Packed to 2 so the layout is unambiguous:
    /// <c>fVirt</c> at offset 0, <c>key</c> at 2, <c>cmd</c> at 4, six bytes total.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct ACCEL {
        internal byte FVirt;
        internal ushort Key;
        internal ushort Cmd;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MENUITEMINFOW {
        internal uint CbSize;
        internal uint FMask;
        internal uint FType;
        internal uint FState;
        internal uint WID;
        internal IntPtr HSubMenu;
        internal IntPtr HbmpChecked;
        internal IntPtr HbmpUnchecked;
        internal IntPtr DwItemData;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? DwTypeData;
        internal uint Cch;
        internal IntPtr HbmpItem;

        /// <summary>A zeroed struct with <c>cbSize</c> already filled in.</summary>
        internal static MENUITEMINFOW Create() =>
            new() { CbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HDHITTESTINFO {
        internal POINT Point;
        internal uint Flags;
        internal int Item;
    }

    // --- Messages -----------------------------------------------------------------------

    internal const int WM_COMMAND = 0x0111;
    internal const int WM_CONTEXTMENU = 0x007B;
    internal const int WM_INITMENUPOPUP = 0x0117;
    internal const int WM_MENUCHAR = 0x0120;
    internal const int WM_ENTERMENULOOP = 0x0211;
    internal const int WM_EXITMENULOOP = 0x0212;

    // --- WinEvents ----------------------------------------------------------------------

    internal const uint EVENT_OBJECT_FOCUS = 0x8005;
    internal const int OBJID_CLIENT = -4;
    internal const int CHILDID_SELF = 0;

    /// <summary><c>LVM_FIRST + 31</c>. Returns the ListView header HWND, or zero outside Details view.</summary>
    internal const uint LVM_GETHEADER = 0x101F;

    /// <summary><c>HDM_FIRST + 6</c>.</summary>
    internal const uint HDM_HITTEST = 0x1206;

    // --- AppendMenu / EnableMenuItem / CheckMenuItem flags -------------------------------

    internal const uint MF_BYCOMMAND = 0x00000000;
    internal const uint MF_BYPOSITION = 0x00000400;
    internal const uint MF_STRING = 0x00000000;
    internal const uint MF_SEPARATOR = 0x00000800;
    internal const uint MF_POPUP = 0x00000010;
    internal const uint MF_CHECKED = 0x00000008;
    internal const uint MF_UNCHECKED = 0x00000000;
    internal const uint MF_ENABLED = 0x00000000;
    internal const uint MF_GRAYED = 0x00000001;
    internal const uint MF_DISABLED = 0x00000002;

    // --- MENUITEMINFO fState -------------------------------------------------------------

    internal const uint MFS_ENABLED = 0x00000000;
    internal const uint MFS_GRAYED = 0x00000003;
    internal const uint MFS_DISABLED = MFS_GRAYED;
    internal const uint MFS_CHECKED = 0x00000008;
    internal const uint MFS_UNCHECKED = 0x00000000;

    // --- MENUITEMINFO fType --------------------------------------------------------------

    internal const uint MFT_STRING = 0x00000000;
    internal const uint MFT_SEPARATOR = 0x00000800;

    /// <summary>Draws a bullet instead of a checkmark when the item is checked.</summary>
    internal const uint MFT_RADIOCHECK = 0x00000200;

    internal const uint MFT_RIGHTORDER = 0x00002000;
    internal const uint MFT_RIGHTJUSTIFY = 0x00004000;

    // --- MENUITEMINFO fMask --------------------------------------------------------------

    internal const uint MIIM_STATE = 0x00000001;
    internal const uint MIIM_ID = 0x00000002;
    internal const uint MIIM_SUBMENU = 0x00000004;
    internal const uint MIIM_TYPE = 0x00000010;
    internal const uint MIIM_STRING = 0x00000040;
    internal const uint MIIM_FTYPE = 0x00000100;

    // --- TrackPopupMenuEx flags -----------------------------------------------------------

    internal const uint TPM_LEFTBUTTON = 0x0000;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_LEFTALIGN = 0x0000;
    internal const uint TPM_RIGHTALIGN = 0x0008;
    internal const uint TPM_TOPALIGN = 0x0000;
    internal const uint TPM_HORIZONTAL = 0x0000;
    internal const uint TPM_VERTICAL = 0x0040;

    /// <summary>Makes <see cref="TrackPopupMenuEx"/> return the chosen command id directly.</summary>
    internal const uint TPM_RETURNCMD = 0x0100;

    // --- ACCEL fVirt flags -----------------------------------------------------------------

    internal const byte FVIRTKEY = 0x01;
    internal const byte FNOINVERT = 0x02;
    internal const byte FSHIFT = 0x04;
    internal const byte FCONTROL = 0x08;
    internal const byte FALT = 0x10;

    // --- Header hit-test result flags -------------------------------------------------------

    internal const uint HHT_NOWHERE = 0x0001;
    internal const uint HHT_ONHEADER = 0x0002;
    internal const uint HHT_ONDIVIDER = 0x0004;
    internal const uint HHT_ONDIVOPEN = 0x0008;
}
