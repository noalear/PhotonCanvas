using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.LeatherFabric;

/// <summary>
/// Leather and fabric pieces from the selected outlines: seam allowance, stitch holes, notches and
/// mirrored pairs, previewed live on the work area. Cancel leaves the design as it was.
/// </summary>
public sealed class SewnPatternWindow : Window
{
    readonly IAddonHost _host;
    readonly Document _doc;
    readonly DocState _base;
    readonly IReadOnlyCollection<Guid> _selection;
    readonly ComboBox _material = Combo(SewnMaterials.All.Select(m => m.Name));
    readonly TextBlock _warning = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly ComboBox _drawn = Combo(["The cut edge", "The sewing line"]);
    readonly NumericUpDown _allowance = Number(0.5m, 50, 0.5m, "0.##");
    readonly ComboBox _holes = Combo(["None", "Round holes", "Slanted slots (pricking iron)", "Diamonds (diamond chisel)"]);
    readonly NumericUpDown _pitch = Number(1, 30, 0.1m, "0.##");
    readonly NumericUpDown _size = Number(0.3m, 10, 0.1m, "0.##");
    readonly CheckBox _inside = new() { Content = "Stitch round cut-outs inside the pieces too" };
    readonly ComboBox _stitchLine = Combo(["Show it, don't burn it", "Burn a light groove"]);
    readonly CheckBox _notches = new() { Content = "Notches in the seam allowance" };
    readonly CheckBox _mirror = new() { Content = "Also make the mirror image (a left and a right)" };
    readonly ComboBox _recipe = Combo([]);
    readonly List<(Material M, Recipe R)?> _recipes = [];
    readonly TextBlock _info = Hint();
    readonly Button _ok = new() { Content = "Make pieces", IsDefault = true, Classes = { "primary" }, MinWidth = 110 };
    readonly Control _holeRows;
    List<Guid> _made = [];
    bool _done, _loading;

    static ComboBox Combo(IEnumerable<string> items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items.ToList() };

    string LastFile => Path.Combine(_host.DataFolder, "last-options.json");

    public SewnPatternWindow(IAddonHost host)
    {
        _host = host;
        _doc = host.Document;
        _base = _doc.State;
        _selection = [.. _doc.Selection];
        Title = "Leather and fabric";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var sources = SewnBuild.Sources(_base, _selection);
        var start = sources.Select(SewnBuild.OptionsOf).FirstOrDefault(x => x is not null)
            ?? SewnBuild.Load(TryRead(LastFile)) ?? SewnOptions.For(SewnMaterial.VegTan);

        _holeRows = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Field("Stitch length", _pitch, tip: "From one hole to the next (mm). Pricking irons are commonly 2.7, 3, 3.38, 3.85, 4 or 5 mm. Each side is spaced evenly, as close to this as a whole number of stitches allows, with a hole in every corner."),
                Field("Hole size", _size, tip: "Round holes: their diameter. Slots and diamonds: their length (mm); slots are 0.38 times as wide, diamonds 0.45 times."),
                _inside,
            },
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        _ok.Click += (_, _) => Finish();
        cancel.Click += (_, _) => Close();
        Closed += (_, _) => { if (!_done) _doc.CancelEdit(); };

        Content = new ScrollViewer
        {
            MaxHeight = 760,
            Content = new Border
            {
                Padding = new Thickness(18),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        Field("Material", _material, tip: "What the pieces are cut from. Changing it sets the usual settings for it."),
                        _warning,
                        Field("You drew", _drawn, tip: "The cut edge: the stitching goes inside it (usual for leather). The sewing line: the cut edge goes outside it (usual for sewing patterns)."),
                        Field("Seam allowance", _allowance, tip: "From the cut edge to the stitch line (mm). Leather is usually stitched 3–4 mm in from the edge; sewing patterns use 10 mm or 15 mm (5/8\")."),
                        Field("Stitch holes", _holes, tip: "Holes for sewing by hand, made by the laser instead of a punch or pricking iron. None for machine sewing."),
                        _holeRows,
                        Field("Stitch line", _stitchLine, tip: "Show it: on the Pattern layer, never burned. Burn a light groove: on a Score layer, like a stitching groover, so the thread sits below the surface (leather only)."),
                        _notches,
                        _mirror,
                        Field("Cut with", _recipe, tip: "Settings from your material library (Leather or Fabric) for the cut and hole layers, or leave the layers' own settings."),
                        _info,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, _ok } },
                    },
                },
            },
        };

        _loading = true;
        Show(start);
        _loading = false;
        _material.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            // A new material brings its usual settings; the mirror and library choice stay.
            var keep = Read();
            _loading = true;
            Show(SewnOptions.For(Material) with { Mirror = keep.Mirror, StitchInside = keep.StitchInside });
            _loading = false;
            Preview();
        };
        foreach (var c in new[] { _drawn, _holes, _stitchLine, _recipe }) c.SelectionChanged += (_, _) => Preview();
        foreach (var n in new[] { _allowance, _pitch, _size }) n.ValueChanged += (_, _) => Preview();
        foreach (var b in new[] { _inside, _notches, _mirror }) b.IsCheckedChanged += (_, _) => Preview();
        _doc.BeginEdit();
        Opened += (_, _) => Preview();
    }

    static string? TryRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; } catch (Exception) { return null; }
    }

    SewnMaterial Material => SewnMaterials.All[Math.Max(0, _material.SelectedIndex)].Kind;

    void Show(SewnOptions o)
    {
        _material.SelectedIndex = Math.Max(0, SewnMaterials.All.ToList().FindIndex(m => m.Kind == o.Material));
        _drawn.SelectedIndex = (int)o.Drawn;
        _allowance.Value = (decimal)o.AllowanceMm;
        _holes.SelectedIndex = (int)o.Holes;
        _pitch.Value = (decimal)o.PitchMm;
        _size.Value = (decimal)o.HoleSizeMm;
        _inside.IsChecked = o.StitchInside;
        _stitchLine.SelectedIndex = (int)o.StitchLine;
        _notches.IsChecked = o.Notches;
        _mirror.IsChecked = o.Mirror;
        FillRecipes(o.RecipeId);
    }

    /// <summary>The library's cut recipes for the material's category (Leather or Fabric).</summary>
    void FillRecipes(Guid? chosen)
    {
        var category = SewnMaterials.Info(Material).Category;
        _recipes.Clear();
        _recipes.Add(null);
        var names = new List<string> { "Leave the layers' settings" };
        foreach (var m in _host.Library.Materials.Where(m => m.Category == category).OrderBy(m => m.Label))
            foreach (var r in m.Recipes.Where(r => r.Purpose == RecipePurpose.Cut))
            {
                _recipes.Add((m, r));
                names.Add(string.IsNullOrWhiteSpace(r.Title) ? m.Label : $"{m.Label}: {r.Title}");
            }
        _recipe.ItemsSource = names;
        int at = chosen is { } id ? _recipes.FindIndex(x => x?.R.Id == id) : 0;
        _recipe.SelectedIndex = Math.Max(0, at);
    }

    SewnOptions Read() => new SewnOptions
    {
        Material = Material,
        Drawn = (DrawnLine)Math.Max(0, _drawn.SelectedIndex),
        AllowanceMm = (double)(_allowance.Value ?? 3.5m),
        Holes = (HoleStyle)Math.Max(0, _holes.SelectedIndex),
        PitchMm = (double)(_pitch.Value ?? 4m),
        HoleSizeMm = (double)(_size.Value ?? 1.6m),
        StitchInside = _inside.IsChecked == true,
        StitchLine = (StitchLineShow)Math.Max(0, _stitchLine.SelectedIndex),
        Notches = _notches.IsChecked == true,
        Mirror = _mirror.IsChecked == true,
        RecipeId = _recipe.SelectedIndex > 0 && _recipe.SelectedIndex < _recipes.Count ? _recipes[_recipe.SelectedIndex]?.R.Id : null,
    }.Clamped();

    (ProcessSettings, MaterialRef)? Recipe()
    {
        int i = _recipe.SelectedIndex;
        if (i <= 0 || i >= _recipes.Count || _recipes[i] is not { } x) return null;
        return (x.R.Settings, new MaterialRef(x.M.Id, x.M.Label, x.R.Id, x.R.Title));
    }

    void Preview()
    {
        if (_loading) return;
        var o = Read();
        var info = SewnMaterials.Info(o.Material);
        _warning.Text = info.Note;
        _warning.Foreground = info.Danger ? Brush("Danger") : Brush("TextMuted");
        _warning.FontWeight = info.Danger ? FontWeight.SemiBold : FontWeight.Normal;
        _holeRows.IsVisible = o.Holes != HoleStyle.None;
        _ok.IsEnabled = !info.Never;
        if (info.Never)
        {
            _doc.UpdateEdit(_base);
            _made = [];
            _info.Text = "Nothing is made for PVC or vinyl.";
            return;
        }
        var field = new Rect2(0, 0, _host.Machine.FieldW, _host.Machine.FieldH);
        var r = SewnBuild.Apply(_base, _selection, o, field, Recipe());
        _doc.UpdateEdit(r.State);
        _made = r.Made;
        var lines = new List<string>();
        if (r.Pieces > 0)
            lines.Add($"{r.Pieces} piece{(r.Pieces == 1 ? "" : "s")}" + (o.Holes != HoleStyle.None ? $", {r.Holes} stitch hole{(r.Holes == 1 ? "" : "s")}." : "."));
        lines.AddRange(r.Problems);
        if (r.OffBed) lines.Add("Some of it is off the work area: move it before sending.");
        if (Recipe() is null && r.Pieces > 0) lines.Add("Set the power and speed for your material on the new layers (or pick library settings under Cut with) before sending.");
        _info.Text = string.Join("\n", lines);
    }

    void Finish()
    {
        var o = Read();
        if (SewnMaterials.Info(o.Material).Never || _made.Count == 0) return;
        _done = true;
        try { Directory.CreateDirectory(_host.DataFolder); File.WriteAllText(LastFile, SewnBuild.Save(o)); } catch (Exception) { }
        _doc.EndEdit(SewnMaterials.Info(o.Material).Fabric ? "Fabric pieces" : "Leather pieces");
        _doc.Select(_made);
        Close();
    }
}
