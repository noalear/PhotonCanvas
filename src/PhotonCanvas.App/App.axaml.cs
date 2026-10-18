using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace PhotonCanvas.App;

public partial class App : Application
{
    bool _showingError;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Colours and units first: every window picks them up as it's made.
            var settings = AppSettings.Load();
            AppTheme.Apply(this, settings.Theme == "Light");
            Units.Inches = settings.Inches;
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (desktop.Args is [var path, ..] && File.Exists(path)) window.OpenPathOnStartup = path;
            InstallSafetyNet(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// A bug in one handler must not throw away someone's work: unexpected
    /// errors on the UI thread are logged and reported, and the app keeps running.
    /// </summary>
    void InstallSafetyNet(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AppLog.Error("ui", e.Exception);
            e.Handled = true;
            if (_showingError || desktop.MainWindow is not { IsVisible: true } owner) return;
            _showingError = true;
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await Dialogs.Message(owner, "Something went wrong",
                        $"{PhotonCanvas.Core.AppIdentity.ProductName} hit an unexpected problem but is still running; your design is untouched.\n\n" +
                        $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\nDetails were saved to {AppLog.CurrentFile}");
                }
                finally { _showingError = false; }
            });
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("background task", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLog.Error("fatal", ex);
        };
    }
}
