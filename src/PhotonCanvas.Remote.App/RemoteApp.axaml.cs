using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PhotonCanvas.Remote.App;

/// <summary>PhotonCanvas Remote: watch and run the laser from the phone, through the remote server on the user's computer.</summary>
public partial class RemoteApp : Application
{
    /// <summary>What the phone (or computer) it runs on can do. Set by the Android app or the desktop window before start.</summary>
    public static IPhone Phone { get; set; } = new NoPhone();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>The app's screen, once it's up.</summary>
    public static MainView? View { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        // A mistake in the app shows as a message to copy, rather than closing it.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            if (View is { } v) v.ShowReport("Something went wrong", e.Exception.ToString());
            else CrashLog.Write(Phone.DataFolder, e.Exception);
        };
        View = new MainView(Phone);
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = View;
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Window
            {
                Title = "PhotonCanvas Remote",
                Width = 412,
                Height = 880,
                Content = View,
                Background = (Avalonia.Media.IBrush)Resources["Bg0"]!,
            };
        base.OnFrameworkInitializationCompleted();
    }
}
