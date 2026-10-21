using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The inlay maker: the selected shapes as a pocket engraved into the board (as deep as the veneer,
/// from the etch test) and the piece cut from the veneer (with the cut test's kerf), so it sits flush.
/// </summary>
public sealed class InlayWindow : Window
{
    readonly Document _doc;
    readonly IReadOnlyList<PathShape> _shapes;
    readonly NumericUpDown _veneer = Number(0.1m, 10, 0.05m, "0.0#"), _gap = Number(0, 1, 0.01m, "0.0#"), _etch = Number(0.01m, 2, 0.005m, "0.0##"), _kerf = Number(0, 1, 0.01m, "0.0#");
    readonly ComboBox _etchWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch }, _cutWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly List<(Material M, Recipe R)> _etches, _cuts;
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };

    public InlayWindow(Document doc, MaterialLibrary? lib, IReadOnlyList<PathShape> shapes)
    {
        _doc = doc;
        _shapes = shapes;
        Title = "Inlay";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var mats = lib?.Materials ?? [];
        _etches = [.. mats.SelectMany(m => m.Recipes.Where(r => r.EtchMmPerPass is > 0).Select(r => (m, r)))];
        _cuts = [.. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => (m, r)))];
        _etchWith.ItemsSource = _etches.Select(x => $"{x.M.Label} · {x.R.Title} ({x.R.EtchMmPerPass:0.###} mm a pass)").Prepend("Default engrave settings").ToList();
        _cutWith.ItemsSource = _cuts.Select(x => $"{x.M.Label} · {x.R.Title}" + (x.R.Settings.KerfMm > 0 ? $" (kerf {x.R.Settings.KerfMm:0.###} mm)" : "")).Prepend("Default cut settings").ToList();
        _veneer.Value = 0.6m; _gap.Value = 0.05m; _etch.Value = 0.15m; _kerf.Value = 0.1m;
        _etchWith.SelectedIndex = _etches.Count > 0 ? 1 : 0;
        _cutWith.SelectedIndex = _cuts.Count > 0 ? 1 : 0;
        _etchWith.SelectionChanged += (_, _) => { if (Etch() is { } e && e.R.EtchMmPerPass is { } v) _etch.Value = (decimal)v; Plan(); };
        _cutWith.SelectionChanged += (_, _) => { if (Cut() is { } c) _kerf.Value = (decimal)c.R.Settings.KerfMm; Plan(); };
        if (Etch() is { } first && first.R.EtchMmPerPass is { } fv) _etch.Value = (decimal)fv;
        if (Cut() is { } firstCut) _kerf.Value = (decimal)firstCut.R.Settings.KerfMm;
        foreach (var n in new[] { _veneer, _gap, _etch, _kerf }) n.ValueChanged += (_, _) => Plan();
        var make = new Button { Content = "Make the pocket and the piece", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        make.Click += (_, _) => Make();
        Content = new StackPanel
        {
            Spacing = 10, Margin = new(16),
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"The {shapes.Count} selected shape{(shapes.Count == 1 ? "" : "s")} as an inlay: a pocket engraved into the board, as deep as the veneer, and the piece cut from the veneer to fit it, so it sits flush." },
                Field("Veneer (mm)", _veneer, tip: "The thickness of the wood (or other material) that goes in: the pocket goes this deep."),
                Field("Gap each side (mm)", _gap, tip: "Room round the piece in the pocket, for glue and the wood moving. 0.03–0.08 mm is snug."),
                Field("Engrave with", _etchWith, tip: "The pocket's settings: from your etch-rate test, so its depth a pass is known."),
                Field("Depth a pass (mm)", _etch),
                Field("Cut with", _cutWith, tip: "The veneer's cut settings: the kerf from your cut test makes the piece come out the drawn size."),
                Field("Kerf (mm)", _kerf),
                _plan,
                make,
                Hint("The pocket goes on this page, on the board. The piece goes on a page of its own (\"Inlay piece\"), in the same place: put the veneer in, switch to that page, and cut."),
            },
        };
        Plan();
    }

    (Material M, Recipe R)? Etch() => _etchWith.SelectedIndex >= 1 && _etchWith.SelectedIndex - 1 < _etches.Count ? _etches[_etchWith.SelectedIndex - 1] : null;
    (Material M, Recipe R)? Cut() => _cutWith.SelectedIndex >= 1 && _cutWith.SelectedIndex - 1 < _cuts.Count ? _cuts[_cutWith.SelectedIndex - 1] : null;

    InlayOptions Options() => new()
    {
        VeneerMm = (double)(_veneer.Value ?? 0.6m),
        GapMm = (double)(_gap.Value ?? 0.05m),
        EtchMmPerPass = (double)(_etch.Value ?? 0.15m),
        PocketSettings = Etch() is { } e ? e.R.Settings.ApplyTo(Layer.Default(0, LayerMode.Fill)) : null,
        PieceSettings = (Cut() is { } c ? c.R.Settings.ApplyTo(Layer.Default(0, LayerMode.Cut)) : Layer.Default(0, LayerMode.Cut)) with { KerfMm = (double)(_kerf.Value ?? 0) },
    };

    void Plan()
    {
        var o = Options();
        int passes = Inlay.Passes(o);
        _plan.Text = $"Pocket: {passes} pass{(passes == 1 ? "" : "es")} of {o.EtchMmPerPass:0.###} mm, about {passes * o.EtchMmPerPass:0.##} mm deep for {o.VeneerMm:0.##} mm of veneer, {o.GapMm:0.##} mm bigger each side. " +
                     $"Piece: cut at the drawn size (kerf {(double)(_kerf.Value ?? 0):0.###} mm allowed for)." +
                     (Etch() is null ? "\nThe depth a pass is a guess without an etch-rate test (Laser → Etch rate): the piece may sit proud or sunk." : "");
    }

    void Make()
    {
        try
        {
            _doc.Commit("Inlay", Inlay.Make(_doc.State, _shapes, Options()));
            Close();
        }
        catch (InvalidOperationException e) { _plan.Text = e.Message; }
    }
}
