using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>
/// The job as a strip, left to right in the order it runs: one block per layer, as wide as
/// the time it takes, in the layer's colour. When passes run over the whole job, each round
/// is shown (with its pause). While a job runs, the part already done is lit. Hover a block
/// for its details; click it to bring up that layer.
/// </summary>
public sealed class JobTimeline : Control
{
    sealed record Seg(Guid? Layer, double Seconds, string Label, string Tip, Color Colour, bool NewRound = false);

    List<Seg> _segs = [];
    readonly List<(Rect Rect, Seg Seg)> _laid = [];
    Guid _active;
    Guid? _hover;
    string? _empty = "Nothing to run yet";
    double? _progress;

    /// <summary>A layer's block was clicked.</summary>
    public event Action<Guid>? LayerClicked;

    /// <summary>The mouse is over a block (its description), or left (null).</summary>
    public event Action<string?>? Hovered;

    public JobTimeline()
    {
        Height = 26;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>Done so far, 0–1, while a job runs; null otherwise.</summary>
    public double? Progress
    {
        get => _progress;
        set { _progress = value; InvalidateVisual(); }
    }

    public void Show(BuiltJob? job, DocState state, Guid active, string? problem)
    {
        _active = active;
        _segs = [];
        if (job is null || job.LayerTimes.Count == 0)
        {
            _empty = problem ?? "Nothing to run yet";
            InvalidateVisual();
            return;
        }
        _empty = null;
        Layer? Find(Guid id) => state.FindLayer(id);
        static Color ColourOf(Layer l) => Color.FromRgb((byte)(l.Color >> 16), (byte)(l.Color >> 8), (byte)l.Color);
        static string Mode(Layer l) => l.Mode switch { LayerMode.Cut => "Cut", LayerMode.Score => "Score", LayerMode.Fill => "Engrave", _ => "Picture" };
        int rounds = job.LayerTimes.Max(t => t.Passes);
        if (job.RoundDelayS is { } delay && rounds > 1)
        {
            for (int r = 1; r <= rounds; r++)
            {
                bool first = true;
                foreach (var t in job.LayerTimes.Where(t => t.Passes >= r))
                {
                    if (Find(t.LayerId) is not { } l) continue;
                    _segs.Add(new(l.Id, t.PassSeconds, UiKit.Clock(t.PassSeconds),
                        $"{l.Name} · round {r} of {rounds}\n{Mode(l)} at {l.Laser.Power:0.#}%, {l.Laser.Speed:0.#} mm/s · {UiKit.Clock(t.PassSeconds)} this round",
                        ColourOf(l), NewRound: first && r > 1));
                    first = false;
                }
                if (r < rounds && delay > 0)
                    _segs.Add(new(null, delay, "", $"Pause of {delay:0.#} s before round {r + 1}", Color.Parse("#3A404C")));
            }
        }
        else
        {
            foreach (var t in job.LayerTimes)
            {
                if (Find(t.LayerId) is not { } l) continue;
                string passes = t.Passes > 1 ? $" ×{t.Passes}" : "";
                _segs.Add(new(l.Id, t.TotalSeconds, $"{l.Name}{passes} · {UiKit.Clock(t.TotalSeconds)}",
                    $"{l.Name}\n{Mode(l)} at {l.Laser.Power:0.#}%, {l.Laser.Speed:0.#} mm/s{passes} · {UiKit.Clock(t.TotalSeconds)}",
                    ColourOf(l)));
            }
        }
        InvalidateVisual();
    }

    void Layout(Rect area)
    {
        _laid.Clear();
        if (_segs.Count == 0) return;
        const double Gap = 2, RoundGap = 9, Min = 5;
        int rounds = _segs.Count(s => s.NewRound);
        double avail = Math.Max(0, area.Width - Gap * (_segs.Count - 1) - (RoundGap - Gap) * rounds);
        double total = _segs.Sum(s => s.Seconds);
        var widths = _segs.Select(s => total > 0 ? avail * s.Seconds / total : avail / _segs.Count).ToArray();
        // Tiny blocks get a minimum width; the rest share what's left in proportion.
        var small = widths.Select(w => w < Min).ToArray();
        double fixedW = small.Count(x => x) * Min;
        double restSeconds = _segs.Where((_, i) => !small[i]).Sum(s => s.Seconds);
        for (int i = 0; i < widths.Length; i++)
            widths[i] = small[i] ? Min : restSeconds > 0 ? Math.Max(Min, (avail - fixedW) * _segs[i].Seconds / restSeconds) : Min;
        double x = area.X;
        for (int i = 0; i < _segs.Count; i++)
        {
            if (_segs[i].NewRound) x += RoundGap - Gap;
            _laid.Add((new Rect(x, area.Y, widths[i], area.Height), _segs[i]));
            x += widths[i] + Gap;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var area = new Rect(Bounds.Size);
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#0E1014")), null, area, 6, 6);
        var typeface = new Typeface(FontFamily.Default);
        if (_empty is not null)
        {
            var t = new FormattedText(_empty, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11.5, new SolidColorBrush(Color.Parse("#8F98A6")))
            { MaxTextWidth = Math.Max(1, area.Width - 16), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(t, new Point(8, (area.Height - t.Height) / 2));
            return;
        }
        Layout(area);
        double done = _progress is { } p ? area.Width * Math.Clamp(p, 0, 1) : double.PositiveInfinity;
        foreach (var (r, seg) in _laid)
        {
            bool hot = seg.Layer is { } id && id == _hover;
            var c = seg.Colour;
            double alpha = seg.Layer is null ? 1 : hot ? 1 : 0.78;
            if (_progress is not null && r.X >= done) alpha *= 0.35;
            ctx.DrawRectangle(new SolidColorBrush(c, alpha), null, r, 4, 4);
            if (_progress is not null && r.X < done && r.Right > done)
                ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#990E1014")), null, new Rect(done, r.Y, r.Right - done, r.Height), 0, 0);
            if (seg.Layer == _active && seg.Layer is not null)
                ctx.DrawRectangle(Brushes.White, null, new Rect(r.X + 2, r.Bottom - 3, Math.Max(1, r.Width - 4), 2), 1, 1);
            if (seg.NewRound) // a notch between rounds
                ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#8F98A6")), null, new Rect(r.X - 5.5, r.Y + 6, 2, r.Height - 12), 1, 1);
            if (seg.Label.Length == 0 || r.Width < 26) continue;
            double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
            var ink = lum > 0.6 ? Color.Parse("#14171C") : Colors.White;
            var t = new FormattedText(seg.Label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, new SolidColorBrush(ink))
            { MaxTextWidth = r.Width - 8, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            if (t.Width < 4) continue;
            ctx.DrawText(t, new Point(r.X + 4, r.Y + (r.Height - t.Height) / 2));
        }
        if (_progress is not null)
            ctx.DrawRectangle(Brushes.White, null, new Rect(Math.Min(done, area.Width - 2), -1, 2, area.Height + 2));
    }

    Seg? At(Point p) => _laid.FirstOrDefault(x => x.Rect.Contains(p)).Seg;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var seg = At(e.GetPosition(this));
        if (seg?.Layer != _hover || seg is null)
        {
            _hover = seg?.Layer;
            InvalidateVisual();
        }
        ToolTip.SetTip(this, seg?.Tip);
        Hovered?.Invoke(seg?.Tip.Replace('\n', ' '));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        Hovered?.Invoke(null);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (At(e.GetPosition(this))?.Layer is { } id) LayerClicked?.Invoke(id);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        InvalidateVisual();
        return base.ArrangeOverride(finalSize);
    }
}
