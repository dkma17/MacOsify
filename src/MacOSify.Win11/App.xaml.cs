using Microsoft.UI.Xaml;

namespace MacOSify.Win11;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "MacOSifyWin11", "crash.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
            System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now}] {e.Exception}\n");
        }
        catch { }
        System.Diagnostics.Debug.WriteLine(e.Exception);
    }
}
