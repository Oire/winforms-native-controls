namespace Oire.WinForms.NativeControls;

/// <summary>
/// Tracks whether a popup menu is currently being tracked by <c>TrackPopupMenuEx</c>, so
/// <see cref="NativeMenuBar.Rebuild"/> and <see cref="NativeContextMenu.Rebuild"/> can refuse
/// to destroy an <c>HMENU</c> Windows is still displaying.
/// </summary>
/// <remarks>
/// UI-thread only by design. <c>TrackPopupMenuEx</c> runs a nested message loop on the calling
/// thread, and every caller here is on the WinForms UI thread, so the pump serializes access
/// and no locking is needed. The counter is nesting-aware because a popup can open a submenu
/// popup of its own.
/// </remarks>
internal static class MenuTrackingScope {
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
