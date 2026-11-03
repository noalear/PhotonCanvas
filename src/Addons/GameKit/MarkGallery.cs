using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>A little picture of a mark: a piece of wood (or none) with what's engraved on it and the lines scored.</summary>
public sealed class MarkPicture : Control
{
    readonly StreamGeometry? _cut, _etched, _lines;
    readonly Rect _box;

    public MarkPicture(PathsD? cut, PathsD etched, List<PathD>? lines, double width, double height)
    {
        Width = width;
        Height = height;
        var all = new PathsD([.. cut ?? [], .. etched, .. lines ?? []]);
        var b = all.Count > 0 ? Geo.Bounds(all) : new RectD(0, 0, 1, 1);
        _box = new Rect(b.left, -b.bottom, Math.Max(1e-6, b.right - b.left), Math.Max(1e-6, b.bottom - b.top));
        if (cut is { Count: > 0 }) _cut = PiecesView.Stream(cut, true);
        if (etched.Count > 0) _etched = PiecesView.Stream(etched, true);
        if (lines is { Count: > 0 }) _lines = PiecesView.Stream(lines, false);
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext ctx)
    {
        double pad = _cut is null ? 3 : 1;
        double k = Math.Min((Bounds.Width - 2 * pad) / _box.Width, (Bounds.Height - 2 * pad) / _box.Height);
        if (!(k > 0)) return;
        double ox = (Bounds.Width - _box.Width * k) / 2 - _box.X * k, oy = (Bounds.Height - _box.Height * k) / 2 - _box.Y * k;
        if (_cut is null) ctx.DrawRectangle(PiecesView.Wood, null, new Rect(Bounds.Size), 4, 4);
        using (ctx.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy)))
        {
            if (_cut is not null) ctx.DrawGeometry(PiecesView.Wood, new Pen(PiecesView.EdgeBrush, 1 / k), _cut);
            if (_etched is not null) ctx.DrawGeometry(PiecesView.Burnt, null, _etched);
            if (_lines is not null) ctx.DrawGeometry(null, new Pen(PiecesView.ScoreBrush, 0.9 / k), _lines);
        }
    }
}

/// <summary>Tiles to pick from, each a picture with a name under it; the one picked is outlined.</summary>
public sealed class MarkGallery : WrapPanel
{
    readonly List<Button> _tiles = [];
    int _selected = -1;

    /// <summary>A tile was clicked (its index).</summary>
    public event Action<int>? Picked;

    public int Count => _tiles.Count;

    public void SetTiles(IEnumerable<(Control Picture, string Name, string? Tip)> tiles, double tileWidth)
    {
        Children.Clear();
        _tiles.Clear();
        foreach (var (picture, name, tip) in tiles)
        {
            int index = _tiles.Count;
            var label = new TextBlock { Text = name, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = tileWidth, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Button
            {
                Width = tileWidth + 12,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 4, 4),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Content = new StackPanel { Spacing = 3, Children = { picture, label } },
            };
            ToolTip.SetTip(b, tip ?? name);
            AutomationName(b, name);
            b.Click += (_, _) => Picked?.Invoke(index);
            _tiles.Add(b);
            Children.Add(b);
        }
        Select(_selected);
    }

    static void AutomationName(Control c, string name) => Avalonia.Automation.AutomationProperties.SetName(c, name);

    /// <summary>Outlines tile <paramref name="index"/> (none for −1) without raising <see cref="Picked"/>.</summary>
    public void Select(int index)
    {
        _selected = index;
        for (int i = 0; i < _tiles.Count; i++)
        {
            _tiles[i].BorderBrush = i == index ? UiKit.Brush("Accent") : Brushes.Transparent;
            if (i == index) _tiles[i].Background = UiKit.Brush("AccentSoft");
            else _tiles[i].ClearValue(Button.BackgroundProperty);
        }
    }
}
