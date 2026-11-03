using Avalonia.Controls;
using Avalonia.Layout;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Extensions;
using PhotonCanvas.Doc.Geometry;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.RotaryFinish;

/// <summary>
/// Finish stacked models on the rotary: "Finish it on the rotary" and its settings in 3D to slices
/// (the stacked-layer settings) put a dowel hole up the middle of every slice and, with the slices,
/// a page that engraves the glued stack down to the model's shape on the rotary. A job check says
/// when the work mode doesn't match the page.
/// </summary>
public sealed class RotaryFinishAddon : IAddon
{
    public void Start(IAddonHost host)
    {
        host.AddToSlot("stackslice.stacked", ctx => ctx.Data is IStackSliceSlot slot ? new RotaryFinishPanel(slot, host.Library) : null);
        host.AddJobHook(new RotaryPageCheck());
    }

    public void Stop() { }
}

/// <summary>A rotary finishing page wants the rotary, at the size it was made for.</summary>
sealed class RotaryPageCheck : IJobHook
{
    public string Name => "Rotary finish";

    public IEnumerable<string> Check(DocState state, JobOptions options)
    {
        if (RotaryPage.MadeFor(state) is not { } d) yield break;
        if (options.Mode is not WorkMode.Rotary r)
        {
            yield return $"this page is for the rotary: choose Laser → Work mode → Rotary (a chuck on the dowel), Object Ø {d:0.#} mm.";
            yield break;
        }
        double want = Math.Min(Math.PI * d, WorkModes.MaxCircumferenceMm);
        if (Math.Abs(r.CircumferenceMm - want) > 0.5)
            yield return $"the page was made for Object Ø {d:0.#} mm in Work mode (it's set to {r.CircumferenceMm / Math.PI:0.#} mm): set it to {d:0.#} mm, or the engraving lands at the wrong angles.";
        if (r.Direction < 0)
            yield return "the rotary is set to rollers: a stack isn't round, so hold it by its dowel in a chuck.";
        double drop = state.Layers.Where(l => l.Output && state.ShapesOn(l.Id).Any()).Select(l => l.FocusOffsetMm).DefaultIfEmpty(0).Max();
        if (drop > Job.MaxFocusOffsetMm)
            yield return $"the head comes down {drop:0.#} mm for the innermost focus height while the widest parts still turn under it: make sure there's that much room under the head before you start.";
    }
}

/// <summary>The settings, in one 3D to slices window.</summary>
sealed class RotaryFinishPanel : StackPanel
{
    readonly IStackSliceSlot _slot;
    readonly CheckBox _on = new() { Content = "Finish it on the rotary" };
    readonly NumericUpDown _dowel = Number(2, 30, 0.5m, "0.#"), _pass = Number(0.01m, 2, 0.01m, "0.0##"), _band = Number(0.2m, 5, 0.1m, "0.#"),
        _deepest = Number(0.2m, 20, 0.5m, "0.#"), _wall = Number(0.5m, 10, 0.5m, "0.#"), _detail = Number(0.1m, 2, 0.05m, "0.0#");
    readonly CheckBox _right = new() { Content = "Slice 1 on the right" };
    readonly ComboBox _with = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly List<(Material M, Recipe R)> _etches;
    readonly StackPanel _settings;

    public RotaryFinishPanel(IStackSliceSlot slot, MaterialLibrary? library)
    {
        _slot = slot;
        Spacing = 10;
        _dowel.Value = 6; _pass.Value = 0.1m; _band.Value = 1; _deepest.Value = 3; _wall.Value = 2; _detail.Value = 0.25m;
        _etches = [.. (library?.Materials ?? []).SelectMany(m => m.Recipes.Where(r => r.EtchMmPerPass is > 0 && r.Settings.Mode == LayerMode.Fill).Select(r => (m, r)))];
        _with.ItemsSource = _etches.Select(x => $"{x.M.Label} · {x.R.Title} ({x.R.EtchMmPerPass:0.###} mm a pass)").Prepend("Default engrave settings").ToList();
        _with.SelectedIndex = _etches.Count > 0 ? 1 : 0;
        if (Etch() is { R.EtchMmPerPass: { } first }) _pass.Value = (decimal)first;
        ToolTip.SetTip(_on, "A dowel hole up the middle of every slice, and a page that engraves the glued stack down to the model's shape on the rotary, turning on the dowel.");
        ToolTip.SetTip(_right, "The stack goes on the rotary with its bottom slice on the right (the model's top to the left). Off: slice 1 on the left.");
        _settings = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Field("Centre dowel Ø", _dowel, tip: "The dowel the rotary's chuck holds, up the middle of the stack. Glue it in. A notch in its hole points to the side that faces the laser at the start."),
                Field("Engrave with", _with, tip: "Settings from the material library with a measured depth a pass (Laser → Etch rate test). Every focus height uses them."),
                Field("Depth a pass (mm)", _pass, tip: "How deep one pass goes on a flat piece with these settings. Nearer the dowel the lines are closer together, so the passes there go deeper: that's allowed for."),
                Field("Focus band (mm)", _band, tip: "How far the surface may be from the focus. The stack isn't round, so the surface is split into bands by how far it is from the dowel, and each band gets its own focus height."),
                Field("Deepest (mm)", _deepest, tip: "Never engraves deeper than this anywhere; spots that need more are left to sand."),
                Field("Wall kept (mm)", _wall, tip: "At least this much is left over the dowel hole, the pin holes and a hollow, so the engraving never breaks through."),
                Field("Detail (mm)", _detail, tip: "How finely the surface is mapped. Smaller follows the shape closer but takes longer to make."),
                _right,
            },
        };
        Children.Add(_on);
        Children.Add(_settings);
        _on.IsCheckedChanged += (_, _) => Changed();
        _right.IsCheckedChanged += (_, _) => Changed();
        _with.SelectionChanged += (_, _) => { if (Etch() is { R.EtchMmPerPass: { } e }) _pass.Value = (decimal)e; Changed(); };
        foreach (var n in new[] { _dowel, _pass, _band, _deepest, _wall, _detail }) n.ValueChanged += (_, _) => Changed();
        _settings.IsVisible = false;
    }

    (Material M, Recipe R)? Etch() => _with.SelectedIndex >= 1 && _with.SelectedIndex <= _etches.Count ? _etches[_with.SelectedIndex - 1] : null;

    void Changed()
    {
        _settings.IsVisible = _on.IsChecked == true;
        if (_on.IsChecked != true) { _slot.SetModifier(this, null); return; }
        var engrave = Etch() is { } e
            ? e.R.Settings.ApplyTo(Layer.Default(0, LayerMode.Fill)) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) }
            : Layer.Default(0, LayerMode.Fill);
        _slot.SetModifier(this, new RotaryFinishSlices(new RotaryFinishOptions
        {
            DowelMm = (double)(_dowel.Value ?? 6),
            PassDepthMm = (double)(_pass.Value ?? 0.1m),
            FocusBandMm = (double)(_band.Value ?? 1),
            MaxDepthMm = (double)(_deepest.Value ?? 3),
            WallMm = (double)(_wall.Value ?? 2),
            DetailMm = (double)(_detail.Value ?? 0.25m),
            BottomOnRight = _right.IsChecked == true,
        }, engrave));
    }
}
