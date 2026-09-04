using System.ComponentModel;

namespace Oire.WinForms.NativeControls;

/// <summary>
/// A <see cref="ListView"/> that presents itself to assistive technology as the native list it
/// actually is, instead of as the half-implemented table WinForms reports.
/// </summary>
/// <remarks>
/// <para>
/// A WinForms <c>ListView</c> in Details view really is a <c>SysListView32</c>, and screen
/// readers have carried dedicated handling for that control for decades: they read every
/// column, and they let the user move between them. WinForms then layers a UI Automation
/// provider on top which reports the control as <c>ControlType.Table</c>, and that provider is
/// incomplete in a way that matters.
/// </para>
/// <para>
/// Measured on .NET 10. The provider advertises the Grid and Table patterns, reports the right
/// dimensions and the right column headers, and every individual cell exposes a correct
/// <c>GridItemPattern</c> and <c>TableItemPattern</c>. But <c>GridPattern.GetItem(row, column)</c>
/// on the container returns empty, typeless elements. That call is how a screen reader walks to
/// a cell, so the reader enters table mode, asks for a cell, gets nothing usable back, and falls
/// through to the row's <c>Name</c> — which is only the first column. The result, confirmed by
/// ear with JAWS, NVDA and Narrator, is a list where only the first column can be read and
/// column navigation goes nowhere.
/// </para>
/// <para>
/// Declining to answer <c>WM_GETOBJECT</c> for <c>UiaRootObjectId</c> makes UI Automation fall
/// back to the MSAA bridge, where the control is a plain list with one named item per row, and
/// screen readers use their own <c>SysListView32</c> support again. Nothing is lost at the MSAA
/// layer: role, name and per-row items are identical either way.
/// </para>
/// <para>
/// If a future framework release fixes <c>GetItem</c>, set
/// <see cref="AccessibilityMode"/> to <see cref="ListAccessibilityMode.Table"/> to get the
/// richer table semantics back — per-cell column headers and column navigation are genuinely
/// better than a flat list, when they work.
/// </para>
/// </remarks>
public class NativeListView: ListView {
    private const int WM_GETOBJECT = 0x003D;

    /// <summary>
    /// <c>UiaRootObjectId</c>. UI Automation passes this in <c>lParam</c> to ask a window for a
    /// native UIA provider; returning zero means "there isn't one here".
    /// </summary>
    private const int UiaRootObjectId = -25;

    private ListAccessibilityMode _accessibilityMode = ListAccessibilityMode.List;

    /// <summary>
    /// Whether to expose WinForms' UI Automation provider. Defaults to
    /// <see cref="ListAccessibilityMode.List"/>.
    /// </summary>
    /// <remarks>
    /// Changing this after the handle exists recreates it, because assistive technology caches
    /// what a window reported the first time it asked.
    /// </remarks>
    [DefaultValue(ListAccessibilityMode.List)]
    [Category("Accessibility")]
    [Description("Whether the list is announced as a native list (default) or as a WinForms table.")]
    public ListAccessibilityMode AccessibilityMode {
        get => _accessibilityMode;
        set {
            if (_accessibilityMode == value) {
                return;
            }

            _accessibilityMode = value;
            if (IsHandleCreated) {
                RecreateHandle();
            }
        }
    }

    /// <inheritdoc />
    protected override void WndProc(ref Message m) {
        if (_accessibilityMode == ListAccessibilityMode.List
            && m.Msg == WM_GETOBJECT
            && (int)m.LParam.ToInt64() == UiaRootObjectId) {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }
}
