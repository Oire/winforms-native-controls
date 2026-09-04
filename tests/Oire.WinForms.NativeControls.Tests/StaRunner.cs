using System.Runtime.ExceptionServices;

namespace Oire.WinForms.NativeControls.Tests;

/// <summary>
/// Runs a test body on a dedicated single-threaded-apartment thread.
/// </summary>
/// <remarks>
/// xUnit's worker threads are MTA, and anything that creates a <c>Form</c> HWND needs STA.
/// The menu-bar tests need a real form handle because <c>SetMenu</c> and the window subclass
/// operate on one, so they borrow a thread instead of faking the interop away.
/// </remarks>
internal static class StaRunner {
    internal static void Run(Action body) {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() => {
            try {
                body();
            } catch (Exception ex) {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
