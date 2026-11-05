using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static PhotonCanvas.Remote.App.Ui;

namespace PhotonCanvas.Remote.App;

/// <summary>Every job the machine ran while the server was on, with a picture before and after.</summary>
public sealed class HistoryPage : UserControl
{
    readonly MainView _host;
    readonly Session _s;
    readonly StackPanel _list = new() { Spacing = 8 };
    readonly TextBlock _line = Text("", "muted");

    public HistoryPage(MainView host, Session s)
    {
        _host = host;
        _s = s;
        Content = new ScrollViewer { Content = new StackPanel { Margin = new(12), Spacing = 8, Children = { _line, _list } } };
    }

    /// <summary>The tab was opened (or a job just ended): fetch the list again.</summary>
    public async void Opened()
    {
        try
        {
            var items = await _s.Remote.HistoryAsync(60);
            _list.Children.Clear();
            _line.Text = items.Count == 0
                ? "No jobs yet. Every job the machine runs (from the phone, the desktop app or its own screen) shows up here, with a picture before and after."
                : "Tap a picture to see it full size.";
            foreach (var h in items) _list.Children.Add(Item(h));
        }
        catch (RemoteError e) { _line.Text = e.Message; }
    }

    Control Item(HistoryItem h)
    {
        var thumbs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (url, label) in new[] { (h.Before, "Before"), (h.After, "After") })
        {
            if (url is null) continue;
            var img = new Image { Width = 76, Height = 57, Stretch = Stretch.UniformToFill };
            var box = new Border { CornerRadius = new(6), ClipToBounds = true, Background = Brushes.Black, Child = img };
            thumbs.Children.Add(box);
            _ = Fill(img, url);
            box.PointerReleased += async (_, _) =>
            {
                try { _host.ShowPicture(await _s.Remote.ImageAsync(url, 1600), $"{h.Design}: {label.ToLowerInvariant()}, {h.StartedAt.ToLocalTime():g}"); }
                catch (RemoteError e) { _host.Toast(e.Message); }
            };
        }
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (h.EndedAt is not null) badges.Children.Add(Badge(h.Finished ? "Finished" : "Stopped", h.Finished ? "Ok" : "Warn"));
        if (h.StartedFromPhone) badges.Children.Add(Badge("Started from phone", "Accent"));
        // A design the phone loaded, still on the computer: the same again.
        Button? again = null;
        if (h.ProjectId is { } id)
        {
            again = Btn("Run again");
            again.MinHeight = 36;
            again.HorizontalAlignment = HorizontalAlignment.Left;
            again.Padding = new(14, 4);
            again.Click += (_, _) => new DesignSheet(_host, _s, new ProjectItem(id, h.Design, "", DateTimeOffset.Now, false)).Open();
        }
        return new Border
        {
            Classes = { "card" },
            Padding = new(10),
            Child = new Grid
            {
                ColumnDefinitions = new("Auto,*"),
                ColumnSpacing = 12,
                Children =
                {
                    thumbs,
                    new StackPanel
                    {
                        Spacing = 3,
                        Children =
                        {
                            Text(h.Design).With(t => t.FontWeight = FontWeight.SemiBold),
                            Text($"{h.StartedAt.ToLocalTime():g}{(h.TookS is { } took ? $" · {Clock(took)}" : " · running")}", "muted"),
                            Text(h.SourceText, "muted").With(t => t.FontSize = 12),
                            badges,
                            (Control?)again ?? new Panel(),
                        },
                    }.With(p => Grid.SetColumn(p, 1)),
                },
            },
        };
    }

    async Task Fill(Image img, string url)
    {
        try { img.Source = Picture(await _s.Remote.ImageAsync(url, 240)); }
        catch (RemoteError) { }
    }

    static Border Badge(string text, string colour) => new()
    {
        Background = Brush("Bg2"),
        CornerRadius = new(6),
        Padding = new(7, 2),
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush(colour) },
    };
}
