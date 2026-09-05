using System.Runtime.InteropServices;

namespace Oire.WinForms.NativeControls;

/// <summary>
/// The <c>SysListView32</c> message surface: window creation, the <c>LVM_*</c> messages that
/// drive a report-mode list, and the <c>LVN_*</c> notifications it sends back.
/// </summary>
/// <remarks>
/// Separate from <see cref="Win32Interop"/>, which covers menus. Nothing here is shared with
/// that file and keeping them apart keeps either one readable.
/// </remarks>
internal static class ListViewInterop {
    // --- Window creation -----------------------------------------------------------------

    internal const string WindowClass = "SysListView32";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr CreateWindowExW(
        uint exStyle, [MarshalAs(UnmanagedType.LPWStr)] string className,
        [MarshalAs(UnmanagedType.LPWStr)] string? windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveWindow(
        IntPtr hWnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr GetFocus();

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    [StructLayout(LayoutKind.Sequential)]
    internal struct INITCOMMONCONTROLSEX {
        internal uint DwSize;
        internal uint DwICC;
    }

    /// <summary><c>ICC_LISTVIEW_CLASSES</c>: list view and header.</summary>
    internal const uint IccListViewClasses = 0x00000001;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowTextW(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string text);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr handle);

    /// <summary>Hands the control a font; the caller owns the handle and must outlive it.</summary>
    internal const uint WM_SETFONT = 0x0030;

    /// <summary>Suspends and resumes drawing, for bulk updates.</summary>
    internal const uint WM_SETREDRAW = 0x000B;

    internal const uint WM_NOTIFY = 0x004E;
    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_GETDLGCODE = 0x0087;

    // --- Window styles -------------------------------------------------------------------

    internal const uint WS_CHILD = 0x40000000;
    internal const uint WS_VISIBLE = 0x10000000;
    internal const uint WS_TABSTOP = 0x00010000;
    internal const uint WS_BORDER = 0x00800000;
    internal const uint WS_EX_CLIENTEDGE = 0x00000200;
    internal const uint WS_EX_LAYOUTRTL = 0x00400000;

    internal const uint LVS_REPORT = 0x0001;
    internal const uint LVS_SINGLESEL = 0x0004;

    /// <summary>
    /// Keep the selection visible when the control loses focus. Must be part of the creation
    /// style: setting it afterwards is ignored until the window is recreated.
    /// </summary>
    internal const uint LVS_SHOWSELALWAYS = 0x0008;

    internal const uint LVS_EX_FULLROWSELECT = 0x00000020;
    internal const uint LVS_EX_LABELTIP = 0x00004000;
    internal const uint LVS_EX_DOUBLEBUFFER = 0x00010000;
    internal const uint LVS_EX_HEADERDRAGDROP = 0x00000010;

    // --- Messages ------------------------------------------------------------------------

    private const uint LVM_FIRST = 0x1000;

    internal const uint LVM_DELETEALLITEMS = LVM_FIRST + 9;
    internal const uint LVM_DELETEITEM = LVM_FIRST + 8;
    internal const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
    internal const uint LVM_ENSUREVISIBLE = LVM_FIRST + 19;
    internal const uint LVM_HITTEST = LVM_FIRST + 18;
    internal const uint LVM_GETNEXTITEM = LVM_FIRST + 12;
    internal const uint LVM_SETITEMSTATE = LVM_FIRST + 43;
    internal const uint LVM_GETITEMSTATE = LVM_FIRST + 44;
    internal const uint LVM_GETSELECTEDCOUNT = LVM_FIRST + 50;
    internal const uint LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
    internal const uint LVM_GETITEMW = LVM_FIRST + 75;
    internal const uint LVM_SETITEMW = LVM_FIRST + 76;
    internal const uint LVM_INSERTITEMW = LVM_FIRST + 77;
    internal const uint LVM_SETITEMTEXTW = LVM_FIRST + 116;
    internal const uint LVM_GETCOLUMNW = LVM_FIRST + 95;
    internal const uint LVM_SETCOLUMNW = LVM_FIRST + 96;
    internal const uint LVM_INSERTCOLUMNW = LVM_FIRST + 97;
    internal const uint LVM_DELETECOLUMN = LVM_FIRST + 28;
    internal const uint LVM_GETCOLUMNWIDTH = LVM_FIRST + 29;
    internal const uint LVM_SETCOLUMNWIDTH = LVM_FIRST + 30;
    internal const uint LVM_SETINSERTMARK = LVM_FIRST + 166;
    internal const uint LVM_GETHEADER = LVM_FIRST + 31;
    internal const uint LVM_GETITEMRECT = LVM_FIRST + 14;

    internal const uint HDM_GETITEMW = 0x1200 + 11;
    internal const uint HDM_SETITEMW = 0x1200 + 12;

    // --- Item and column fields ----------------------------------------------------------

    internal const uint LVIF_TEXT = 0x0001;
    internal const uint LVIF_STATE = 0x0008;
    internal const uint LVIF_PARAM = 0x0004;

    internal const uint LVIS_FOCUSED = 0x0001;
    internal const uint LVIS_SELECTED = 0x0002;

    internal const uint LVCF_FMT = 0x0001;
    internal const uint LVCF_WIDTH = 0x0002;
    internal const uint LVCF_TEXT = 0x0004;
    internal const uint LVCF_SUBITEM = 0x0008;

    internal const int LVCFMT_LEFT = 0x0000;
    internal const int LVCFMT_RIGHT = 0x0001;
    internal const int LVCFMT_CENTER = 0x0002;

    /// <summary><c>LVNI_ALL</c> combined with the state to search for.</summary>
    internal const uint LVNI_ALL = 0x0000;
    internal const uint LVNI_FOCUSED = 0x0001;
    internal const uint LVNI_SELECTED = 0x0002;

    internal const uint HDI_FORMAT = 0x0004;
    internal const int HDF_SORTUP = 0x0400;
    internal const int HDF_SORTDOWN = 0x0200;

    // --- Notifications -------------------------------------------------------------------

    private const int LVN_FIRST = -100;

    internal const int LVN_ITEMCHANGED = LVN_FIRST - 1;
    internal const int LVN_COLUMNCLICK = LVN_FIRST - 8;
    internal const int LVN_BEGINDRAG = LVN_FIRST - 9;
    internal const int LVN_KEYDOWN = LVN_FIRST - 55;

    internal const int NM_CUSTOMDRAW = -12;
    internal const int NM_DBLCLK = -3;
    internal const int NM_RETURN = -4;

    // --- Custom draw ----------------------------------------------------------------------

    internal const uint CDDS_PREPAINT = 0x00000001;
    internal const uint CDDS_ITEM = 0x00010000;
    internal const uint CDDS_ITEMPREPAINT = CDDS_ITEM | CDDS_PREPAINT;

    internal const int CDRF_DODEFAULT = 0x00000000;
    internal const int CDRF_NEWFONT = 0x00000002;
    internal const int CDRF_NOTIFYITEMDRAW = 0x00000020;

    // --- Structs -------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct LVITEMW {
        internal uint Mask;
        internal int Item;
        internal int SubItem;
        internal uint State;
        internal uint StateMask;
        internal IntPtr Text;
        internal int TextMax;
        internal int Image;
        internal IntPtr LParam;
        internal int Indent;
        internal int GroupId;
        internal uint Columns;
        internal IntPtr PuColumns;
        internal IntPtr PiColFmt;
        internal int Group;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct LVCOLUMNW {
        internal uint Mask;
        internal int Fmt;
        internal int Cx;
        internal IntPtr Text;
        internal int TextMax;
        internal int SubItem;
        internal int Image;
        internal int Order;
        internal int CxMin;
        internal int CxDefault;
        internal int CxIdeal;
    }

    /// <summary><c>LVIR_BOUNDS</c>: the whole row, icon and label together.</summary>
    internal const int LVIR_BOUNDS = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVHITTESTINFO {
        internal Win32Interop.POINT Point;
        internal uint Flags;
        internal int Item;
        internal int SubItem;
        internal int Group;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVINSERTMARK {
        internal uint CbSize;
        internal uint DwFlags;
        internal int Item;
        internal uint DwReserved;
    }

    /// <summary><c>LVIM_AFTER</c>: place the mark after the item rather than before it.</summary>
    internal const uint LVIM_AFTER = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMHDR {
        internal IntPtr HwndFrom;
        internal IntPtr IdFrom;
        internal int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMCUSTOMDRAW {
        internal NMHDR Hdr;
        internal uint DrawStage;
        internal IntPtr Hdc;
        internal RECT Rc;
        internal IntPtr ItemSpec;
        internal uint ItemState;
        internal IntPtr ItemLParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVCUSTOMDRAW {
        internal NMCUSTOMDRAW Nmcd;
        internal uint ClrText;
        internal uint ClrTextBk;
        internal int SubItem;
        internal uint ItemType;
        internal uint ClrFace;
        internal int IconEffect;
        internal int IconPhase;
        internal int PartId;
        internal int StateId;
        internal RECT RcText;
        internal uint Align;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLISTVIEW {
        internal NMHDR Hdr;
        internal int Item;
        internal int SubItem;
        internal uint NewState;
        internal uint OldState;
        internal uint Changed;
        internal Win32Interop.POINT PtAction;
        internal IntPtr LParam;
    }

    /// <summary>Packed to 1 byte, as <c>commctrl.h</c> declares it.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NMLVKEYDOWN {
        internal NMHDR Hdr;
        internal ushort VKey;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct HDITEMW {
        internal uint Mask;
        internal int Cxy;
        internal IntPtr Text;
        internal IntPtr Bitmap;
        internal int TextMax;
        internal int Fmt;
        internal IntPtr LParam;
        internal int Image;
        internal int Order;
        internal uint Type;
        internal IntPtr PvFilter;
        internal uint State;
    }

    // --- OLE drag and drop ----------------------------------------------------------------

    /// <summary>
    /// Registers a window as a drop target. WinForms registers the container, but the list
    /// window covers it, and OLE resolves a drop against the window under the cursor.
    /// </summary>
    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int RegisterDragDrop(IntPtr hWnd, IOleDropTarget target);

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int RevokeDragDrop(IntPtr hWnd);

    /// <summary>A screen point, passed by value as the OLE interface declares it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTL {
        internal int X;
        internal int Y;
    }

    /// <summary>
    /// <c>IDropTarget</c>. Declared here rather than taken from a framework assembly because
    /// the managed <c>System.Windows.Forms.IDropTarget</c> is a different, higher-level thing.
    /// </summary>
    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleDropTarget {
        [PreserveSig]
        int OleDragEnter(IntPtr dataObject, int keyState, POINTL point, ref int effect);

        [PreserveSig]
        int OleDragOver(int keyState, POINTL point, ref int effect);

        [PreserveSig]
        int OleDragLeave();

        [PreserveSig]
        int OleDrop(IntPtr dataObject, int keyState, POINTL point, ref int effect);
    }

    // --- Message senders -----------------------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, ref LVITEMW lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, ref LVCOLUMNW lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, ref LVHITTESTINFO lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, ref RECT lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, ref LVINSERTMARK lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, ref HDITEMW lParam);
}
