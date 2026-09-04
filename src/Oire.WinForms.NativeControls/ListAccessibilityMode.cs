namespace Oire.WinForms.NativeControls;

/// <summary>
/// How a <see cref="NativeListView"/> presents itself to assistive technology.
/// </summary>
public enum ListAccessibilityMode {
    /// <summary>
    /// Present as a native list, by declining to serve WinForms' UI Automation provider so that
    /// assistive technology falls back to MSAA. Screen readers then use the handling they have
    /// carried for <c>SysListView32</c> for decades, and read every column. This is the default,
    /// and the reason this control exists.
    /// </summary>
    List,

    /// <summary>
    /// Leave WinForms' UI Automation provider in place, which reports the control as a table.
    /// The stock WinForms behavior. Choose it only if a future framework release fixes the
    /// grid navigation described on <see cref="NativeListView"/>, or if an application genuinely
    /// wants table semantics and has verified them with the screen readers it cares about.
    /// </summary>
    Table,
}
