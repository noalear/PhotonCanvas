using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.App;
using System.Reflection;

/// <summary>The exhaust fan test add-on's window (in its own help/images), as it opens before a machine is connected.</summary>
static class FanTestShots
{
    public static void Run(MainWindow w, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "FanTest", "help", "images");
        Directory.CreateDirectory(folder);
        var ribbon = (Ribbon)w.GetType().GetField("_ribbon", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
        if (!ribbon.Commands.TryGetValue("Machine/Fan test", out var button)) { Console.WriteLine("no Machine/Fan test button (add-on not built?)"); return; }
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        settle(600);
        if (w.OwnedWindows.FirstOrDefault(x => x.GetType().Name == "FanTestWindow") is not { } win) { Console.WriteLine("Fan test didn't open"); return; }
        win.CaptureRenderedFrame()?.Save(Path.Combine(folder, "window-fan-test.png"));
        Console.WriteLine("fan test: window-fan-test.png");
        win.Close();
        settle(200);
    }
}
