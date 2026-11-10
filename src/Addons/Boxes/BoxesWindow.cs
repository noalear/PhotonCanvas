using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Addon.Borders;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>
/// The box maker: the box's style, size and sheet on the left (lid and joints on their own tabs),
/// the box put together in 3D and its pieces on the right, and what was found while making it.
/// </summary>
public sealed class BoxesWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly string? _dataFolder;
    (Guid Id, BoxSpec Spec)? _editing;

    static readonly string[] BoxStyles = ["Finger-jointed box", "Rounded box (bent corners)", "Box with a bending lid"];
    static readonly string[] Lids = ["No lid (a tray)", "Lift-off lid", "Sliding lid", "Hinged lid (on a pin)"];
    static readonly string[] SeamJoins = ["Dovetails", "Jigsaw knobs", "Square fingers", "Straight (glue a strip behind)"];

    readonly ComboBox _style = Combo(BoxStyles), _sizesAre = Combo("The inside", "The outside"), _lid = Combo(Lids), _seam = Combo(SeamJoins), _cutWith = Combo();
    readonly NumericUpDown _w = Number(10, 1000, 5, "0.#"), _d = Number(10, 1000, 5, "0.#"), _h = Number(5, 1000, 5, "0.#"), _t = Number(0.5m, 12, 0.5m, "0.##");
    readonly NumericUpDown _corner = Number(2, 200, 1, "0.#"), _bend = Number(2, 100, 1, "0.#");
    readonly NumericUpDown _across = Number(0, 20, 1, "0"), _along = Number(0, 20, 1, "0");
    readonly NumericUpDown _finger = Number(0, 50, 0.5m, "0.#"), _fit = Number(-0.5m, 1, 0.05m, "0.##");
    readonly NumericUpDown _lip = Number(2, 30, 0.5m, "0.#"), _pin = Number(1, 10, 0.5m, "0.##");
    readonly NumericUpDown _hSpace = Number(0, 10, 0.1m, "0.##"), _hCut = Number(0, 100, 1, "0.#"), _hGap = Number(0, 20, 0.5m, "0.#");
    readonly CheckBox _notch = new() { Content = "Finger notch in the front" };
    readonly CheckBox _border = new() { Content = "Carve a border on the lid" };
    readonly ComboBox _borderDesign = Combo();
    readonly NumericUpDown _borderW = Number(2, 60, 1, "0.#"), _borderDepth = Number(0.2m, 10, 0.1m, "0.#"), _borderInset = Number(0, 50, 0.5m, "0.#");
    readonly TextBlock _cutNote = Hint(), _lidNote = Hint(), _summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Control _cornerRow, _bendRow, _dividerRows, _lipRow, _pinRow, _hingeRows, _borderRows;
    readonly AssembledView _assembled = new();
    readonly PiecesView _pieces = new();
    readonly CheckBox _open = new() { Content = "Open the lid" };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly List<(Material M, Recipe R)> _cuts = [];
    readonly List<string> _designs;
    bool _loading;
    int _generation;
    BoxKit? _kit;
    string? _kitFrom;

    public BoxesWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, BoxSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _dataFolder = dataFolder;
        _editing = editing;
        Title = "Box maker";
        Width = 1120;
        Height = 760;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        // Measured recipes first: those are the ones whose fingers fit.
        _cuts.AddRange((library?.Materials ?? []).SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => (m, r))).OrderByDescending(x => x.r.Settings.KerfMm > 0));
        _cutWith.ItemsSource = _cuts.Select(x => $"{x.M.Label} · {x.R.Title}" + (x.R.Settings.KerfMm > 0 ? $" (kerf {x.R.Settings.KerfMm:0.###} mm)" : " (no kerf measured)")).Prepend("A new cut layer, set up as it comes").ToList();
        _cutWith.SelectedIndex = _cuts.Count > 0 && _cuts[0].R.Settings.KerfMm > 0 ? 1 : 0;
        _designs = [.. BorderPresets.FrameDesigns.Concat(BorderPresets.BandDesigns).Select(d => d.Name).Distinct()];
        _borderDesign.ItemsSource = _designs;

        _cornerRow = Field("Corner radius", _corner, tip: "The radius of the corners, inside. The wall bends round them on a living hinge.");
        _bendRow = Field("Bend radius", _bend, tip: "How round the bend is where the lid goes over the back, inside.");
        _dividerRows = new StackPanel { Spacing = 8, Children = { Heading("Dividers"), Field("Front to back", _across), Field("Side to side", _along), Hint("They slot into each other half way, and their tabs go through the walls and bottom.") } };
        _lipRow = Field("Lip / runners", _lip, tip: "Under a lift-off lid: how wide the lip that fits inside the walls is. For a sliding lid: how far the runners and cap reach in over it.");
        _pinRow = Field("Pin Ø", _pin, tip: "The hinge pin: a dowel, bamboo skewer, nail or a length of 1.75 mm filament. It's glued in the cheeks under the lid and turns in the sides.");
        _hingeRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Heading("Living hinge"),
                Field("Rows apart", _hSpace, tip: "How far apart the rows of slits are. Closer bends more easily but the strips get thinner. 0 works it out from the sheet."),
                Field("Cut length", _hCut, tip: "How long each slit is (0: worked out from the height)."),
                Field("Gap", _hGap, tip: "The sheet left between two slits in a row (0: worked out from the sheet)."),
            },
        };
        _borderRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field("Design", _borderDesign, tip: "A design from the border maker. It's fitted to the lid, with mitred corners (rounded on a rounded box)."),
                Field("Border width", _borderW),
                Field("Carve depth", _borderDepth, tip: "How deep the deepest part of the border is carved (mm)."),
                Field("From the edge", _borderInset, tip: "How far in from the lid's edge the border starts."),
                Hint("The border goes over the lid as a depth map on its own Picture layer (Depth slices), carved before the pieces are cut. To change its design afterwards, right-click it and use Edit border…"),
            },
        };

        var boxTab = Tab("Box",
            Field("Style", _style),
            Field("Sizes are", _sizesAre, tip: "The inside: what fits in it. The outside: how big it is, walls and lid included."),
            Field("Width", _w),
            Field("Depth", _d),
            Field("Height", _h, tip: "Inside: from the bottom to the underside of the lid. Outside: from the table to the top of the lid."),
            _cornerRow, _bendRow,
            Field("Cut with", _cutWith, tip: "Cut settings from your library. The pieces go on a cut layer with them, kerf included, so the fingers come out the width they're drawn and press together snugly."),
            _cutNote,
            Field("Sheet thickness", _t, tip: "Measure your sheet: plywood sold as 3 mm is often 2.7 to 3.2 mm. The fingers are as deep as the sheet is thick."),
            _dividerRows);
        var lidTab = Tab("Lid", Field("Lid", _lid), _lidNote, _lipRow, _pinRow, _notch, _border, _borderRows);
        var jointsTab = Tab("Joints",
            Field("Finger width", _finger, tip: "How wide the fingers at the corners are. 0 makes them about three times the sheet. Each edge gets an odd number, so the corners work out."),
            Field("Fit", _fit, tip: "Play in every joint on top of the kerf: 0.1 looser, -0.05 tighter. With the kerf measured, 0 presses together snugly."),
            Heading("Pieces too big for the bed"),
            Field("Joined with", _seam, tip: "Panels longer than the bed, and a rounded box's long wall, are cut in pieces joined with these. Dovetails and jigsaw knobs lock together; fingers and a straight cut need glue."),
            Hint("Laser cuts go straight through the sheet, so dovetails are used where two pieces join edge to edge in the same plane. The corners are finger jointed: that's what a laser cuts tight."),
            _hingeRows);
        var tabs = new TabControl { Items = { boxTab, lidTab, jointsTab } };
        var left = new Border { Width = 420, Padding = new Thickness(14, 10, 8, 0), Child = tabs };

        var views = new TabControl();
        var put = new Grid { Children = { _assembled } };
        _open.HorizontalAlignment = HorizontalAlignment.Left;
        _open.VerticalAlignment = VerticalAlignment.Top;
        _open.Margin = new Thickness(10, 6, 0, 0);
        put.Children.Add(_open);
        views.Items.Add(new TabItem { Header = "Put together", Content = Framed(put) });
        views.Items.Add(new TabItem { Header = "Pieces", Content = Framed(_pieces) });
        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings } };
        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 6, 14, 0), RowSpacing = 8 };
        right.Children.Add(views);
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 170, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(resultScroll, 1);
        right.Children.Add(resultScroll);

        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new Thickness(14, 8, 14, 14), ColumnSpacing = 12 };
        bottom.Children.Add(_status);
        Grid.SetColumn(_make, 1);
        bottom.Children.Add(_make);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        bottom.Children.Add(close);

        var body = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("*,Auto") };
        body.Children.Add(left);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        body.Children.Add(bottom);
        Content = body;

        Load(editing?.Spec ?? LoadLast() ?? new BoxSpec());
        if (editing is not null) _make.Content = "Update in the design";
        CutWith(false);
        Hook();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _make.Click += (_, _) => Make();
        _open.IsCheckedChanged += (_, _) => _assembled.LidOpen = _open.IsChecked == true ? 1 : 0;
        Closed += (_, _) => { _debounce.Stop(); _generation++; SaveLast(); };
        _ = Rebuild();
    }

    /// <summary>What's being made, as the window shows it.</summary>
    public BoxSpec Spec => Read();

    /// <summary>The box last shown (for the help pictures and the tests).</summary>
    public BoxKit? Kit => _kit;

    /// <summary>Makes the box from the settings shown now (the preview's own timer may not run).</summary>
    public Task RebuildNow() => Rebuild();

    static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    static ComboBox Combo(params string[] items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items.ToList(), SelectedIndex = items.Length > 0 ? 0 : -1 };

    static Border Framed(Control c) => new() { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Margin = new(0, 6, 0, 0), Child = c };

    static TabItem Tab(string header, params Control[] content) => new()
    {
        Header = header,
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 10, 10) }.With(p => p.Children.AddRange(content)),
        },
    };

    // ---- settings ----

    (Material M, Recipe R)? Recipe => _cutWith.SelectedIndex > 0 && _cutWith.SelectedIndex <= _cuts.Count ? _cuts[_cutWith.SelectedIndex - 1] : null;

    /// <summary>The recipe picked: its sheet thickness, and a note on its kerf.</summary>
    void CutWith(bool setThickness)
    {
        if (Recipe is not { } c)
        {
            _cutNote.Text = "The pieces go on a new cut layer. Set its power and speed, and its kerf (More settings) so the fingers fit snugly.";
            return;
        }
        if (setThickness && c.M.ThicknessMm is { } t) _t.Value = (decimal)Math.Clamp(t, 0.5, 12);
        _cutNote.Text = c.R.Settings.KerfMm > 0
            ? $"On a cut layer with \"{c.M.Label}\"'s settings and its {c.R.Settings.KerfMm:0.###} mm kerf: the fingers press together snugly."
            : "These settings have no kerf measured yet, so the joints will be loose: measure it first (Machine → Kerf gauge, or a Cut test).";
    }

    void Load(BoxSpec s)
    {
        _loading = true;
        _style.SelectedIndex = (int)s.Style;
        _sizesAre.SelectedIndex = s.Outside ? 1 : 0;
        _w.Value = (decimal)s.WidthMm; _d.Value = (decimal)s.DepthMm; _h.Value = (decimal)s.HeightMm; _t.Value = (decimal)s.ThicknessMm;
        _corner.Value = (decimal)s.CornerRadiusMm; _bend.Value = (decimal)s.BendRadiusMm;
        _across.Value = s.DividersAcross; _along.Value = s.DividersAlong;
        _finger.Value = (decimal)s.FingerMm; _fit.Value = (decimal)s.FitMm;
        _lid.SelectedIndex = (int)s.Lid;
        _lip.Value = (decimal)s.LipMm; _pin.Value = (decimal)s.PinMm;
        _hSpace.Value = (decimal)s.HingeSpacingMm; _hCut.Value = (decimal)s.HingeCutMm; _hGap.Value = (decimal)s.HingeGapMm;
        _notch.IsChecked = s.FingerNotch;
        _seam.SelectedIndex = (int)s.SeamJoin;
        _border.IsChecked = s.Border;
        _borderDesign.SelectedIndex = Math.Max(0, _designs.IndexOf(s.BorderDesign));
        _borderW.Value = (decimal)s.BorderWidthMm; _borderDepth.Value = (decimal)s.BorderDepthMm; _borderInset.Value = (decimal)s.BorderInsetMm;
        _loading = false;
        Visibility();
    }

    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    BoxSpec Read() => new()
    {
        Style = (BoxStyle)Math.Max(0, _style.SelectedIndex),
        Outside = _sizesAre.SelectedIndex == 1,
        WidthMm = V(_w, 100), DepthMm = V(_d, 70), HeightMm = V(_h, 45), ThicknessMm = V(_t, 3),
        CornerRadiusMm = V(_corner, 15), BendRadiusMm = V(_bend, 10),
        DividersAcross = (int)V(_across, 0), DividersAlong = (int)V(_along, 0),
        FingerMm = V(_finger, 0), FitMm = V(_fit, 0),
        Lid = (LidKind)Math.Max(0, _lid.SelectedIndex),
        LipMm = V(_lip, 6), PinMm = V(_pin, 3),
        HingeSpacingMm = V(_hSpace, 0), HingeCutMm = V(_hCut, 0), HingeGapMm = V(_hGap, 0),
        FingerNotch = _notch.IsChecked == true,
        SeamJoin = (JoinKind)Math.Max(0, _seam.SelectedIndex),
        Border = _border.IsChecked == true,
        BorderDesign = _borderDesign.SelectedItem as string ?? "",
        BorderWidthMm = V(_borderW, 10), BorderDepthMm = V(_borderDepth, 1.5), BorderInsetMm = V(_borderInset, 4),
    };

    /// <summary>Shows only the settings this style and lid use.</summary>
    void Visibility()
    {
        var s = Read();
        _cornerRow.IsVisible = s.Style == BoxStyle.Rounded;
        _bendRow.IsVisible = s.Style == BoxStyle.BendingLid;
        _dividerRows.IsVisible = s.Style != BoxStyle.Rounded;
        _hingeRows.IsVisible = s.Style != BoxStyle.Finger;
        _lid.IsEnabled = s.Style != BoxStyle.BendingLid;
        _lidNote.Text = s.Style switch
        {
            BoxStyle.BendingLid => "This box's lid is its back, bent over the top on a living hinge.",
            BoxStyle.Rounded => "A rounded box has a lift-off lid or none.",
            _ => s.Lid switch
            {
                LidKind.LiftOff => "A lid with a lip under it that fits inside the walls.",
                LidKind.Sliding => "The lid slides out to the front, between runners inside the sides and a cap on top.",
                LidKind.Hinged => "The lid swings up on a pin through the sides and two cheeks under it.",
                _ => "An open tray.",
            },
        };
        _lidNote.IsVisible = true;
        var lid = s.LidUsed;
        _lipRow.IsVisible = lid is LidKind.LiftOff or LidKind.Sliding;
        _pinRow.IsVisible = lid == LidKind.Hinged;
        _notch.IsVisible = lid == LidKind.Hinged || s.Style == BoxStyle.BendingLid;
        _border.IsEnabled = s.HasLid;
        _borderRows.IsVisible = s.HasLid && s.Border;
        _open.IsVisible = lid != LidKind.None;
    }

    void Hook()
    {
        foreach (var n in new[] { _w, _d, _h, _t, _corner, _bend, _across, _along, _finger, _fit, _lip, _pin, _hSpace, _hCut, _hGap, _borderW, _borderDepth, _borderInset })
            n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _style, _sizesAre, _lid, _seam, _borderDesign })
            c.SelectionChanged += (_, _) => Changed();
        foreach (var c in new[] { _notch, _border })
            c.IsCheckedChanged += (_, _) => Changed();
        _cutWith.SelectionChanged += (_, _) => { CutWith(true); Changed(); };
    }

    void Changed()
    {
        if (_loading) return;
        Visibility();
        _debounce.Stop();
        _debounce.Start();
    }

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last-box.json");

    BoxSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? BoxSpec.FromJson(File.ReadAllText(f)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    void SaveLast()
    {
        try
        {
            if (LastFile is not { } f) return;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, Read().ToJson());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ---- preview ----

    async Task Rebuild()
    {
        int gen = ++_generation;
        var spec = Read();
        double bw = _machine.FieldW, bh = _machine.FieldH;
        BoxKit kit;
        try { kit = await Task.Run(() => BoxMaker.Make(spec, bw, bh)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) { _pieces.Show(null, e.Message); _assembled.Show(null, e.Message); }
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = spec.ToJson();
        var findings = new List<Finding>(kit.Findings);
        if (spec.Border && spec.HasLid && kit.Parts.FirstOrDefault(p => p.LidArea is not null)?.LidArea is { } a)
        {
            double inset = Math.Max(0, spec.BorderInsetMm);
            var (_, problem) = Placement.BorderFor(spec, a.right - a.left - 2 * inset, a.bottom - a.top - 2 * inset);
            if (problem is not null) findings.Add(new Finding(Severity.Warn, $"No border on the lid: {problem}"));
        }
        string? bad = findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text;
        _pieces.Show(kit.Ok ? kit : null, bad ?? "Nothing to show.");
        _assembled.Show(kit.Ok ? kit : null, bad ?? "Nothing to show.");
        _assembled.LidOpen = _open.IsChecked == true ? 1 : 0;
        _findings.Children.Clear();
        foreach (var f in findings.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        var o = kit.Outside;
        int n = kit.PieceCount;
        _summary.Text = n == 0 ? "" : $"{n} piece{(n == 1 ? "" : "s")} to cut. Outside it's {o.X:0.#} wide, {o.Z:0.#} deep and {o.Y:0.#} high (mm).";
        _make.IsEnabled = kit.Ok && n > 0;
    }

    static Control FindingRow(Finding f)
    {
        var (mark, brush) = f.Level switch
        {
            Severity.Bad => ("✕", Brush("Danger")),
            Severity.Warn => ("!", Brush("Warn")),
            _ => ("•", Brush("TextMuted")),
        };
        var g = new Grid { ColumnDefinitions = new("20,*") };
        g.Children.Add(new TextBlock { Text = mark, Foreground = brush, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        var t = new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    // ---- putting it in ----

    bool _making;

    async void Make()
    {
        if (_making) return;
        _making = true;
        _make.IsEnabled = false;
        _status.Text = "Putting the pieces in the design…";
        try
        {
            var spec = Read();
            var built = _kit is not null && _kitFrom == spec.ToJson() ? _kit : null;
            Func<Layer, Layer>? cut = Recipe is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            Guid? replace = _editing is { } ed && Placement.CountEverywhere(_doc.State, ed.Id) > 0 ? ed.Id : null;
            var before = _doc.State;
            var machine = _machine;
            var (kit, s, sheets, id, note) = await Task.Run(() =>
            {
                var kit = built ?? BoxMaker.Make(spec, machine.FieldW, machine.FieldH);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text ?? "Nothing to cut.");
                var (s, sheets, id, note) = Placement.Place(before, kit, spec, machine, new Placement.Options { CutSettings = cut, Replace = replace });
                return (kit, s, sheets, id, note);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the box in."; return; }
            _doc.Commit(replace is null ? "Box" : "Update the box", s, Placement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int n = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {n} piece{(n == 1 ? "" : "s")} in the design on \"{Placement.CutLayer}\"" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)." : ".") + (note is null ? "" : " " + note);
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or FormatException)
        {
            AppLog.Error("boxes", ex);
            _status.Text = $"Couldn't put the box in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
