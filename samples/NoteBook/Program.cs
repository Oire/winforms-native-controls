namespace NoteBook;

internal static class Program {
    [STAThread]
    private static void Main() {
        ApplicationConfiguration.Initialize();

        // Follow whatever the user has set in Windows. The list picks its colors and its visual
        // style from this, so running the sample with the system in dark mode or in a
        // high-contrast theme is the way to check that it does.
        Application.SetColorMode(SystemColorMode.System);

        Application.Run(new MainForm());
    }
}
