using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Stencil;

/// <summary>
/// Stencil bridges for the selected letters: bridges placed for you (shortest way across, two per
/// middle), shown in a preview where a click adds or takes away a bridge, and live on the canvas.
/// Opened on letters that already have bridges, it starts from those, so they can be changed or
/// taken out.
/// </summary>
public sealed class StencilWindow : Window
{
    readonly Document _doc;
    readonly string _dataFolder;
    readonly Action<string> _say;
    readonly DocState _base;
    readonly StencilInput _input;
    readonly double _kerf;
    readonly StencilPreview _preview = new() { MinWidth = 360, MinHeight = 300 };
    readonly NumericUpDown _width = Number(0.2m, 10, 0.1m, "0.0#");
    readonly ComboBox _per = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "1", "2", "3", "4" } };
    readonly ComboBox _dir = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { "Shortest way across", "Up and down", "Across", "At an angle" },
    };
    readonly NumericUpDown _angle = Number(-90, 90, 5, "0");
    readonly Control _angleRow;
    readonly CheckBox _addKerf = new();
    readonly CheckBox _useEdge = new() { Content = "The outline round everything is the stencil's edge" };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly TextBlock _warn = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly Button _ok = new() { Content = "Keep bridges", IsDefault = true, Classes = { "primary" }, MinWidth = 110 };
    readonly Settings _settings;

    StencilSheet? _sheet;
    List<Bridge> _auto = [];
    readonly List<Vec2> _added = [], _removed = [];
    List<Bridge> _bridges = [];
    BridgeCheck? _check;
    CancellationTokenSource? _work;
    int _version;
    bool _busy, _done, _ready;

    /// <param name="dataFolder">Where the window remembers its settings.</param>
    /// <param name="say">A line for the status bar.</param>
    public StencilWindow(Document doc, StencilInput input, string dataFolder, Action<string> say)
    {
        _doc = doc;
        _dataFolder = dataFolder;
        _say = say;
        _base = _doc.State;
        _input = input;
        _settings = Settings.Load(dataFolder);
        var o = input.Options ?? _settings.Options;
        var layer = input.Letters.Select(l => _base.FindLayer(l.LayerId)).FirstOrDefault(l => l is not null);
        _kerf = layer?.Mode == LayerMode.Cut ? Math.Max(0, layer.KerfMm) : 0;

        Title = "Stencil bridges";
        Width = 860;
        Height = 520;
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _addKerf.IsChecked = _kerf > 0 && _settings.AddKerf;
        _addKerf.IsVisible = _kerf > 0;
        _addKerf.Content = $"Add the beam width ({StencilEdit.Mm(_kerf)} mm) so they come out this wide";
        _width.Value = (decimal)Math.Clamp(o.WidthMm, 0.2, 10);
        _per.SelectedIndex = Math.Clamp(o.PerMiddle, 1, 4) - 1;
        _dir.SelectedIndex = (int)o.Direction;
        _angle.Value = (decimal)Math.Clamp(o.AngleDeg, -90, 90);
        _angleRow = Field("Angle (°)", _angle, tip: "0 = left–right; positive turns clockwise.");
        _useEdge.IsChecked = input.Edge is not null;
        _useEdge.IsVisible = input.Edge is not null;
        ToolTip.SetTip(_useEdge, "Ticked: the stencil is what's inside that outline (cut out of a bigger sheet). Unticked: it's a letter like the rest.");

        var again = Button("Place them again", tip: "Forget the bridges added or taken away by hand and place them all again");
        again.Click += (_, _) => { _added.Clear(); _removed.Clear(); Recompute(); };
        var takeOut = Button("Take the bridges out", tip: "Put the letters back as they were before they had bridges (a text is a text again)");
        takeOut.IsVisible = input.Bridged.Count > 0;
        takeOut.Click += (_, _) =>
        {
            _done = true;
            _doc.UpdateEdit(StencilEdit.TakeOut(_base, input.Bridged));
            _doc.EndEdit("Take stencil bridges out");
            _say($"Took the bridges out of {input.Bridged.Count} shape{(input.Bridged.Count == 1 ? "" : "s")}.");
            Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => Close();
        _ok.Click += (_, _) => Keep();

        var notCut = layer is not null && layer.Mode != LayerMode.Cut
            ? Hint($"These letters are on a {layer.Mode} layer. A stencil is cut out: put them on a Cut layer.").With(h => h.Foreground = Brush("Warn"))
            : null;
        var side = new StackPanel
        {
            Spacing = 10,
            Width = 300,
            Children =
            {
                Field("Bridge width", _width, tip: "How wide each bridge is (mm). Wider is stronger but shows more. About 1 mm suits card and thin plastic; 1.5–2 mm suits 3 mm wood."),
                _addKerf,
                Field("Per middle", _per, tip: "How many bridges hold each middle. One holds it; two hold it from both sides so it can't twist or flap. Small middles may get fewer."),
                Field("Direction", _dir, tip: "Shortest: straight across the letter where it is thinnest. Up and down: like a classic stencil lettering."),
                _angleRow,
                _useEdge,
                _status,
                _warn,
                Hint("Click in a letter to add a bridge there; click a bridge to take it away. Scroll to zoom, drag with the right button to move, double-click to see it all."),
                Hint("Letters a bridge crosses become plain outlines; open this again on them to change the bridges or take them out."),
            },
        };
        if (notCut is not null) side.Children.Insert(0, notCut);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { again, takeOut, cancel, _ok },
        };
        again.HorizontalAlignment = takeOut.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("*,Auto"), ColumnSpacing = 16, RowSpacing = 12, Margin = new Thickness(16) };
        var frame = new Border { Child = _preview, BorderBrush = Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true };
        grid.Children.Add(frame);
        var scroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;

        _width.ValueChanged += (_, _) => Recompute();
        _per.SelectionChanged += (_, _) => Recompute();
        _dir.SelectionChanged += (_, _) => { _angleRow.IsVisible = _dir.SelectedIndex == 3; Recompute(); };
        _angle.ValueChanged += (_, _) => Recompute();
        _addKerf.IsCheckedChanged += (_, _) => Recompute();
        _useEdge.IsCheckedChanged += (_, _) => { _sheet = null; Recompute(); };
        _preview.Clicked += Click;
        _angleRow.IsVisible = _dir.SelectedIndex == 3;
        Closed += (_, _) =>
        {
            _work?.Cancel();
            if (!_done) _doc.CancelEdit();
        };
        _doc.BeginEdit();
        Opened += (_, _) =>
        {
            _ready = true;
            // Opened on bridged letters: start from their bridges as they are.
            if (input.Existing.Count > 0) _auto = [.. input.Existing];
            Recompute(keepAuto: input.Existing.Count > 0);
        };
    }

    StencilOptions Options => new()
    {
        WidthMm = (double)(_width.Value ?? 1) + (_addKerf.IsChecked == true ? _kerf : 0),
        PerMiddle = _per.SelectedIndex + 1,
        Direction = (BridgeDirection)Math.Max(0, _dir.SelectedIndex),
        AngleDeg = (double)(_angle.Value ?? 0),
    };

    /// <summary>What is saved with the bridged shapes and as the next default: the width as typed.</summary>
    StencilOptions Typed => Options with { WidthMm = (double)(_width.Value ?? 1) };

    IEnumerable<PathShape> LetterShapes =>
        _useEdge.IsChecked == true || _input.Edge is null ? _input.Letters : _input.Letters.Append(_input.Edge);

    async void Recompute(bool keepAuto = false)
    {
        if (!_ready) return;
        _work?.Cancel();
        var cts = _work = new CancellationTokenSource();
        int version = ++_version;
        var o = Options;
        var sheet = _sheet;
        var letters = LetterShapes.Select(l => l.World).ToList();
        var edge = _useEdge.IsChecked == true ? _input.Edge?.World : null;
        var shapes = LetterShapes.ToList();
        var typed = Typed;
        var added = _added.ToList();
        var removed = _removed.ToList();
        var auto = _auto;
        SetBusy(true, sheet is null ? "Finding the middles…" : "Placing the bridges…");
        try
        {
            var (s, a, all, check, state) = await Task.Run(() =>
            {
                var sh = sheet ?? new StencilSheet(letters, edge);
                cts.Token.ThrowIfCancellationRequested();
                // One piece of work on a sheet at a time (a cancelled one may still be finishing).
                lock (sh)
                {
                    var au = keepAuto ? auto : sh.AutoBridges(o, cts.Token);
                    cts.Token.ThrowIfCancellationRequested();
                    var list = Combine(sh, au, added, removed, o);
                    return (sh, au, list, sh.Check(list), Build(list, shapes, typed));
                }
            }, cts.Token);
            if (version != _version) return;
            _sheet = s;
            _auto = a;
            Show(all, check, state);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (version == _version) { _status.Text = $"Couldn't place the bridges: {e.Message}"; SetBusy(false, null); }
        }
    }

    /// <summary>The placed bridges, less the ones taken away by hand, plus the ones clicked in.</summary>
    static List<Bridge> Combine(StencilSheet sheet, IReadOnlyList<Bridge> auto, List<Vec2> added, List<Vec2> removed, StencilOptions o)
    {
        var list = auto.Where(b => !removed.Any(p => b.DistanceTo(p) <= b.WidthMm / 2 + 0.05)).ToList();
        foreach (var p in added)
            if (sheet.BridgeAt(p, o) is { } b) list.Add(b);
        return list;
    }

    /// <summary>The design with these bridges (works off the UI thread: everything it needs is passed in).</summary>
    DocState Build(IReadOnlyList<Bridge> bridges, IReadOnlyList<PathShape> letters, StencilOptions typed) =>
        StencilEdit.Apply(_base, new StencilInput { Letters = letters, Bridged = _input.Bridged, Existing = [] }, bridges, typed);

    void Show(List<Bridge> bridges, BridgeCheck check, DocState state)
    {
        _bridges = bridges;
        _check = check;
        _preview.Show(_sheet);
        _preview.Show(bridges, check);
        _doc.UpdateEdit(state);
        SetBusy(false, null);
        var sheet = _sheet!;
        if (sheet.Middles.Count == 0)
            _status.Text = "Nothing would fall out: none of these letters has a middle (like the inside of an O). Click a letter to add a bridge anyway.";
        else
            _status.Text = $"{sheet.Middles.Count} middle{(sheet.Middles.Count == 1 ? "" : "s")} held by {bridges.Count} bridge{(bridges.Count == 1 ? "" : "s")}.";
        var warn = new List<string>();
        if (check.Loose.Count > 0)
            warn.Add($"{check.Loose.Count} middle{(check.Loose.Count == 1 ? "" : "s")} (red) would still fall out: click across {(check.Loose.Count == 1 ? "its" : "their")} letter to add a bridge.");
        if (check.Long.Count > 0)
            warn.Add($"{check.Long.Count} bridge{(check.Long.Count == 1 ? " is" : "s are")} more than {StencilSheet.LongRatio:0} times as long as {(check.Long.Count == 1 ? "it is" : "they are")} wide (orange) and may bend: make them wider.");
        if (sheet.TinyPieces > 0)
            warn.Add($"{sheet.TinyPieces} sliver{(sheet.TinyPieces == 1 ? "" : "s")} where outlines overlap will fall out; weld overlapping letters first to avoid them.");
        _warn.Text = string.Join("\n", warn);
        _warn.Foreground = check.Loose.Count > 0 ? Brush("Danger") : Brush("Warn");
        _warn.IsVisible = warn.Count > 0;
    }

    void SetBusy(bool busy, string? text)
    {
        _busy = busy;
        _ok.IsEnabled = !busy;
        if (text is not null) _status.Text = text;
    }

    void Click(Vec2 p)
    {
        if (_busy || _sheet is not { } sheet) return;
        lock (sheet) Click(sheet, p);
    }

    void Click(StencilSheet sheet, Vec2 p)
    {
        var o = Options;
        var hit = _preview.Hovered ?? _bridges.Where(b => b.DistanceTo(p) <= b.WidthMm / 2).MinBy(b => b.DistanceTo(p));
        if (hit is not null)
        {
            // Take it away: forget the click that made it, or remember not to place it.
            int i = _added.FindIndex(a => sheet.BridgeAt(a, o) is { } b && b.Equals(hit));
            if (i >= 0) _added.RemoveAt(i);
            else _removed.Add(hit.Middle);
        }
        else if (sheet.BridgeAt(p, o) is not null) _added.Add(p);
        else
        {
            _status.Text = sheet.InLetter(p)
                ? "A bridge can't cross cleanly there (too near a corner or the end of a stroke): click a little way along."
                : "Click inside a letter (the dark part) to add a bridge across it.";
            return;
        }
        var list = Combine(sheet, _auto, _added, _removed, o);
        Show(list, sheet.Check(list), Build(list, [.. LetterShapes], Typed));
    }

    void Keep()
    {
        if (_busy) return;
        _done = true;
        _settings.Options = Typed;
        if (_kerf > 0) _settings.AddKerf = _addKerf.IsChecked == true;
        _settings.Save(_dataFolder);
        if (_bridges.Count == 0 && _input.Bridged.Count == 0) { _doc.CancelEdit(); _say("No bridges to keep."); Close(); return; }
        _doc.EndEdit("Stencil bridges");
        int loose = _check?.Loose.Count ?? 0;
        _say($"Kept {_bridges.Count} bridge{(_bridges.Count == 1 ? "" : "s")}" +
            (loose > 0 ? $"; {loose} middle{(loose == 1 ? "" : "s")} would still fall out." : "; every middle is held."));
        Close();
    }

    /// <summary>The window's remembered choices, in the add-on's own folder.</summary>
    sealed class Settings
    {
        public StencilOptions Options { get; set; } = new();
        public bool AddKerf { get; set; } = true;

        static string PathIn(string folder) => System.IO.Path.Combine(folder, "settings.json");

        public static Settings Load(string folder)
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathIn(folder))) ?? new(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return new(); }
        }

        public void Save(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(PathIn(folder), System.Text.Json.JsonSerializer.Serialize(this));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
