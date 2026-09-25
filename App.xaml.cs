using Microsoft.UI.Xaml;

namespace AiTranslator.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        UnhandledException += (_, eventArgs) =>
        {
            WriteCrashLog(eventArgs.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            WriteCrashLog(eventArgs.ExceptionObject as Exception);
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static void WriteCrashLog(Exception? exception)
    {
        try
        {
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexround",
                "AiTranslator");
            Directory.CreateDirectory(logDirectory);
            File.WriteAllText(Path.Combine(logDirectory, "crash.log"), exception?.ToString() ?? "Unknown error");
        }
        catch
        {
            // A crash logger must never mask the original exception.
        }
    }
}
