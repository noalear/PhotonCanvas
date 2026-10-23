using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>New versions: the check at start, the "a new version is out" notice, and the Updates page in Settings.</summary>
public static class UpdateNotice
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>At start (when allowed): if a newer version is out and wasn't skipped, say so. Quiet when offline.</summary>
    public static async Task CheckAtStart(Window owner, AppSettings settings)
    {
        if (!settings.CheckForUpdates) return;
        var latest = await Updates.LatestAsync(Http);
        if (latest is null || !Updates.Newer(latest.Version, Updates.Current) || latest.Version == settings.SkippedVersion) return;
        AppLog.Write("updates", $"{latest.Version} is out (this is {Updates.Current}).");
        await Offer(owner, settings, latest);
    }

    /// <summary>Check now, and say what was found either way.</summary>
    public static async Task CheckNow(Window owner, AppSettings settings)
    {
        var latest = await Updates.LatestAsync(Http);
        if (latest is null) { await Dialogs.Message(owner, "Updates", "Couldn't reach GitHub to check (offline, or no versions published yet)."); return; }
        if (!Updates.Newer(latest.Version, Updates.Current)) { await Dialogs.Message(owner, "Updates", $"{AppIdentity.ProductName} {Updates.Current} is the latest version."); return; }
        await Offer(owner, settings, latest);
    }

    /// <summary>The notice: what's new, and Download (the file for this computer, or the release's page), Skip this version, or Later.</summary>
    static async Task Offer(Window owner, AppSettings settings, Updates.Release r)
    {
        var w = new Window
        {
            Title = "A new version is out",
            Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("Bg1"), CanResize = false,
        };
        var file = r.ForThisComputer;
        var download = new Button { Content = file is null ? "Open the download page" : "Download", Classes = { "primary" }, MinWidth = 120 };
        var skip = new Button { Content = "Skip this version", MinWidth = 120 };
        var later = new Button { Content = "Later", IsCancel = true, MinWidth = 90 };
        download.Click += (_, _) => { Open(file?.Url ?? r.PageUrl); w.Close(); };
        skip.Click += (_, _) => { settings.SkippedVersion = r.Version; settings.Save(); w.Close(); };
        later.Click += (_, _) => w.Close();
        w.Content = new StackPanel
        {
            Spacing = 10, Margin = new(18),
            Children =
            {
                new TextBlock { Text = $"{AppIdentity.ProductName} {r.Version} is out", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = $"You have {Updates.Current}." + (file is { } f ? $" The download for this computer is {f.Name}: unpack it in place of the old one (your designs, settings and library are kept in your PhotonCanvas folder, not with the program)." : ""), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new ScrollViewer { MaxHeight = 320, Content = new TextBlock { Text = r.Notes.Trim().Length > 0 ? r.Notes.Trim() : "(No notes for this version.)", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5 } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { later, skip, download } },
            },
        };
        await w.ShowDialog(owner);
    }

    static void Open(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { AppLog.Write("updates", e.Message); }
    }

    /// <summary>Settings → Updates: this version, the start-up check on or off, and Check now.</summary>
    public static Control Panel(AppSettings settings, Func<Window?> owner)
    {
        var check = new CheckBox { Content = "Check for a new version at start", IsChecked = settings.CheckForUpdates };
        check.IsCheckedChanged += (_, _) => { settings.CheckForUpdates = check.IsChecked == true; settings.Save(); };
        var now = new Button { Content = "Check now", MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
        now.Click += async (_, _) => { if (owner() is { } o) await CheckNow(o, settings); };
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "UPDATES", Classes = { "h" } },
                new TextBlock { Text = $"This is {AppIdentity.ProductName} {Updates.Current} ({Updates.Platform}).", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                check,
                Hint($"At start, {AppIdentity.ProductName} asks GitHub (github.com/{Updates.Repo}) for the latest version and tells you when there's a newer one. Nothing about you or your designs is sent; like any web request it does reveal your internet address to GitHub. Off: never asks."),
                now,
            },
        };
    }
}
