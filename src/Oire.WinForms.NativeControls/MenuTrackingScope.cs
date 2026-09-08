namespace Oire.WinForms.NativeControls;

/// <summary>
/// Tracks whether a popup menu is currently being tracked by <c>TrackPopupMenuEx</c>, so
/// <see cref="NativeMenuBar.Rebuild"/> and <see cref="NativeContextMenu.Rebuild"/> can refuse
/// to destroy an <c>HMENU</c> Windows is still displaying.
/// </summary>
/// <remarks>
/// Per-thread, because a menu is. <c>TrackPopupMenuEx</c> runs a nested message loop on the
/// calling thread and the <c>HMENU</c> it displays belongs to that thread, so a popup being
/// tracked on one UI thread says nothing about whether another thread may rebuild its own menus.
/// WinForms permits more than one UI thread, each with its own pump, and a process-wide counter
/// would have one of them refusing a legitimate rebuild because an unrelated thread happened to
/// have a menu open.
/// <para>
/// Thread-static also removes the need for locking: each thread sees only its own depth, and a
/// thread's own message pump serializes its access to it. The counter is nesting-aware because
/// a popup can open a submenu popup of its own.
/// </para>
/// </remarks>
internal static class MenuTrackingScope {
    [ThreadStatic]
    private static int _depth;

    /// <summary>True while at least one popup is being tracked.</summary>
    internal static bool IsTracking => _depth > 0;

    /// <summary>Marks the start of a tracked popup. Dispose the result when tracking ends.</summary>
    internal static IDisposable Enter() {
        _depth++;
        return new Scope();
    }

    private sealed class Scope: IDisposable {
        private bool _exited;

        public void Dispose() {
            if (_exited) {
                return;
            }

            _exited = true;
            _depth--;
        }
    }
}
