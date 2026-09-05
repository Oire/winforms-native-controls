namespace Oire.WinForms.NativeControls;

/// <summary>
/// Re-announces the focused control to assistive technologies after a native menu closes.
/// </summary>
/// <remarks>
/// Native menu mode never moves the keyboard focus. The menu runs on top of whatever had
/// focus, and closing it fires no focus event at all, so a screen reader has to <em>ask</em>
/// what has focus rather than being told. Controls that answer that question through their own
/// UI Automation provider get away with it; ones that route accessibility through MSAA instead
/// — <see cref="NativeListView"/> among them — can leave the reader still naming the menu, so
/// the user is told they are in a menu that closed several keystrokes ago. Raising
/// <c>EVENT_OBJECT_FOCUS</c> by hand closes that gap: the reader is told, and stops guessing.
/// </remarks>
internal static class FocusAnnouncer {
    /// <summary>
    /// Announces <paramref name="focus"/> as the focused window, if it still holds focus.
    /// A window that lost focus in the meantime needs no announcement — whatever took it will
    /// have announced itself.
    /// </summary>
    internal static void Announce(IntPtr focus) {
        if (focus == IntPtr.Zero || Win32Interop.GetFocus() != focus) {
            return;
        }

        Win32Interop.NotifyWinEvent(
            Win32Interop.EVENT_OBJECT_FOCUS, focus, Win32Interop.OBJID_CLIENT, Win32Interop.CHILDID_SELF);
    }

    /// <summary>
    /// Announces the focused window once the current message has finished being handled.
    /// </summary>
    /// <remarks>
    /// Only safe where no menu command can be in flight — a popup tracked with
    /// <c>TPM_RETURNCMD</c> posts no <c>WM_COMMAND</c>, so its dismissal has nothing to race.
    /// The menu bar does, and defers through posted messages instead.
    /// </remarks>
    internal static void AnnounceWhenIdle(Control host) {
        ArgumentNullException.ThrowIfNull(host);

        if (!host.IsHandleCreated) {
            return;
        }

        var focus = Win32Interop.GetFocus();
        if (focus == IntPtr.Zero) {
            return;
        }

        host.BeginInvoke(() => Announce(focus));
    }
}
