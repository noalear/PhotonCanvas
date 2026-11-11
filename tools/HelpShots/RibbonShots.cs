using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PhotonCanvas.App;
using PhotonCanvas.Doc;

/// <summary>
/// A picture of every ribbon tab, to check its layout (gaps, lone buttons) without the machine:
/// at a 1920 px window, then again with a rotary owned and in use (Work mode, Scan the part show).
/// Not for the help guide: the pictures go into the folder given (default: a temp folder).
/// </summary>
static class RibbonShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static void Run(string folder, Action pump)
    {
        void Settle(int ms) { for (int i = 0; i < ms / 25; i++) { pump(); Thread.Sleep(25); } }
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-ribbon-data");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Environment.SetEnvironmentVariable(PhotonCanvas.Core.AppIdentity.DataDirVariable, data);
        Directory.CreateDirectory(folder);
        var w = new MainWindow { Width = 1920, Height = 900, ConnectOnOpen = false };
        T F<T>(string name) => (T)typeof(MainWindow).GetField(name, Any)!.GetValue(w)!;
        w.Show();
        Settle(1500);
        var ribbon = F<Ribbon>("_ribbon");
        var settings = F<AppSettings>("_settings");

        void Shoot(string suffix)
        {
            foreach (var tab in ribbon.TabNames.ToList())
            {
                ribbon.Select(tab);
                Settle(400);
                var size = ribbon.Bounds.Size;
                var bmp = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                bmp.Render(ribbon);
                var name = $"ribbon-{string.Concat(tab.Where(char.IsLetterOrDigit)).ToLowerInvariant()}{suffix}.png";
                bmp.Save(Path.Combine(folder, name));
                // How wide the tab is with every label showing (more than the window: some labels go).
                var current = typeof(Ribbon).GetField("_current", Any)!.GetValue(ribbon)!;
                var widths = (double[]?)current.GetType().GetProperty("Widths", Any)!.GetValue(current);
                Console.WriteLine($"{name}\t{(widths is null ? "?" : $"{widths[0]:0} px with every label")}");
            }
        }
        Shoot("");
        settings.OwnsRa2 = true;
        settings.WorkMode = "Rotary";
        settings.RotaryAttachment = 0;
        ribbon.Refresh();
        Settle(400);
        ribbon.Select("Machine");
        Settle(400);
        var size = ribbon.Bounds.Size;
        var rot = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        rot.Render(ribbon);
        rot.Save(Path.Combine(folder, "ribbon-machine-rotary.png"));
        Console.WriteLine("ribbon-machine-rotary.png");
        w.Close();
    }
}
