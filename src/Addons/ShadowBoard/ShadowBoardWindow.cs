using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addons.ShadowBoard;

/// <summary>
/// The shadow board maker: lay the tools on the bed and the camera outlines them (or use shapes
/// already drawn), name them, choose how the board is made, see it, and make it. Nothing changes in
/// the document until Make; Make is one undo step. Only the camera is used: the head never moves.
/// </summary>
public sealed class ShadowBoardWindow : Window
{
    const double MmPerPx = 0.4;

    readonly IShadowBoardHost _host;
    List<ShadowTool> _tools = [];
    readonly List<(CheckBox Use, TextBox Name)> _rows = [];
    ShadowBoardPlan? _plan;
    bool _busyNow;

    // Tools
    readonly NumericUpDown _distance = Number(0, 60, 0.1m, "0.0");
    readonly TextBlock _cameraState = new() { TextWrapping = TextWrapping.Wrap };
    readonly Slider _tolerance = new() { Minimum = 3, Maximum = 60, Value = 20, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _minArea = Number(10, 20000, 10, "0");
    readonly Button _photo = Wide("Take photo and find tools", primary: true);
    readonly Button _useSelection = Wide("Use the selected shapes");
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    readonly TextBlock _found = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _toolList = new() { Spacing = 4 };

    // Board
    readonly ComboBox _style = Combo("Stacked sheets (cut and glue)", "One board, pockets engraved");
    readonly NumericUpDown _depth = Number(0.5m, 100, 0.5m, "0.0#"), _sheet = Number(0.5m, 30, 0.5m, "0.0#"), _etch = Number(0.01m, 2, 0.01m, "0.0##");
    readonly NumericUpDown _gap = Number(0, 10, 0.25m, "0.0#"), _notch = Number(0, 60, 1, "0.#"), _margin = Number(0, 200, 1, "0.#");
    readonly ComboBox _notches = Combo("A notch each side", "One notch, at the front", "No notches");
    readonly NumericUpDown _width = Number(0, 2000, 5, "0.#"), _height = Number(0, 2000, 5, "0.#"), _corner = Number(0, 50, 1, "0.#");
    readonly CheckBox _labels = new() { Content = "Engrave each tool's name", IsChecked = true };
    readonly NumericUpDown _labelSize = Number(2, 30, 0.5m, "0.0#");
    readonly CheckBox _shadow = new() { Content = "Engrave the tools' shadows on the base sheet", IsChecked = true };
    readonly NumericUpDown _pins = Number(0, 12, 0.5m, "0.0#");
    readonly CheckBox _cutOutline = new() { Content = "Cut the board's outline", IsChecked = true };
    readonly ComboBox _cutWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch }, _engraveWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Control _sheetRow, _etchRow, _shadowRow, _pinsRow;

    // Result
    readonly BoardPreview _preview = new() { Height = 380 };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _warnings = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _make = new() { Content = "Make the board", MinWidth = 140, IsEnabled = false, Classes = { "primary" } };

    internal ShadowBoardWindow(IShadowBoardHost host)
    {
        _host = host;
        Title = "Shadow board";
        Width = 1000;
        Height = 760;
        MinWidth = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Res("Bg1") is IBrush bg) Background = bg;

        _distance.Value = host.MeasuredDistanceMm is { } d ? (decimal)Math.Round(d, 1) : null;
        _minArea.Value = 150;
        _depth.Value = 9; _sheet.Value = 3; _etch.Value = 0.15m; _gap.Value = 1.5m; _notch.Value = 22; _margin.Value = 15;
        _width.Value = 0; _height.Value = 0; _corner.Value = 4; _labelSize.Value = 5; _pins.Value = 3;
        _style.SelectedIndex = 0; _notches.SelectedIndex = 0;
        _cutWith.ItemsSource = host.CutRecipes.Select(r => r.Label).Prepend("Default cut settings").ToList();
        _engraveWith.ItemsSource = host.EngraveRecipes.Select(r => r.Label).Prepend("Default engrave settings").ToList();
        _cutWith.SelectedIndex = host.CutRecipes.Count > 0 ? 1 : 0;
        _engraveWith.SelectedIndex = host.EngraveRecipes.Count > 0 ? 1 : 0;
        _engraveWith.SelectionChanged += (_, _) => { if (Engrave()?.EtchMmPerPass is { } e) _etch.Value = (decimal)e; Replan(); };
        if (Engrave()?.EtchMmPerPass is { } first) _etch.Value = (decimal)first;

        _distance.ValueChanged += (_, _) => CheckCamera();
        _photo.Click += async (_, _) => await FindByCamera();
        _useSelection.Click += (_, _) => SetTools(ShadowBoardMaker.FromShapes(_host.SelectedPaths), "from the selected shapes");
        _useSelection.IsEnabled = host.SelectedPaths.Count > 0;
        _useSelection.Content = host.SelectedPaths.Count > 0 ? $"Use the {host.SelectedPaths.Count} selected shape{(host.SelectedPaths.Count == 1 ? "" : "s")}" : "Use the selected shapes (select some first)";
        foreach (var n in new[] { _depth, _sheet, _etch, _gap, _notch, _margin, _width, _height, _corner, _labelSize, _pins }) n.ValueChanged += (_, _) => Replan();
        foreach (var c in new[] { _style, _notches, _cutWith }) c.SelectionChanged += (_, _) => Replan();
        foreach (var c in new[] { _labels, _shadow, _cutOutline }) c.IsCheckedChanged += (_, _) => Replan();
        _make.Click += (_, _) => Make();
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        ToolTip.SetTip(_tolerance, "How different from the bed a colour must be to count as a tool. Raise it if the bed's honeycomb shows up; lower it if shiny or pale tools are missed.");

        _sheetRow = Field("Sheet (mm)", _sheet, "The thickness of one sheet. The depth is rounded up to whole sheets.");
        _etchRow = Field("Depth a pass (mm)", _etch, "How deep one engrave pass goes in this board (from its etch-rate test). The passes come from the depth.");
        _shadowRow = _shadow;
        _pinsRow = Field("Dowel holes (mm)", _pins, "Holes in the four corners of every sheet: push dowels (or cocktail sticks) through them to line the stack up while the glue sets. 0 = none.");

        var settings = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "A drawer insert with a pocket for each tool, finger notches to lift them out, and their names alongside. " +
                           "Lay the tools on the bed where you want them in the drawer, flat side down and apart from each other.",
                },
                Heading("1  TOOLS"),
                Field("Distance (mm)", _distance, "The camera picture only lines up at the right height: the distance from the laser to the bed the tools lie on (or a sheet under them)."),
                _cameraState,
                Field("Tolerance", _tolerance),
                Field("Smallest tool (mm²)", _minArea, "Specks, crumbs and reflections smaller than this are ignored."),
                _photo,
                _useSelection,
                _busy,
                _found,
                _toolList,
                Heading("2  BOARD"),
                Field("Made as", _style, "Stacked sheets: the top sheets have the tools cut through and are glued onto a solid base. One board: the pockets are engraved down into a thick board (slow, but one piece)."),
                Field("Depth (mm)", _depth, "How deep the tools sit. About two thirds of the thickest tool keeps them from sliding about but easy to grab."),
                _sheetRow,
                _etchRow,
                Field("Gap (mm)", _gap, "Room round each tool, each side, so it drops in. 1–2 mm suits a camera outline."),
                Field("Finger notches", _notches),
                Field("Notch size (mm)", _notch, "The notch's diameter: 20–25 mm fits a finger and thumb."),
                Field("Margin (mm)", _margin, "Solid board round the tools."),
                Field("Board width (mm)", _width, "A fixed size, such as the inside of the drawer, centred on the tools. 0 = just big enough."),
                Field("Board depth (mm)", _height),
                Field("Corners (mm)", _corner, "Corner radius of the board."),
                _labels,
                Field("Label size (mm)", _labelSize),
                _shadowRow,
                _pinsRow,
                _cutOutline,
                Field("Cut with", _cutWith, "Settings for cutting the sheets (or the board's outline), from your material library."),
                Field("Engrave with", _engraveWith, "Settings for the labels, the shadows and engraved pockets."),
            },
        };
        var result = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading("3  PREVIEW"),
                new Border { Background = Res("Bg0") as IBrush ?? Brushes.Black, CornerRadius = new(6), Child = _preview, ClipToBounds = true },
                _summary,
                _warnings,
            },
        };
        var grid = new Grid { ColumnDefinitions = new("400,16,*"), RowDefinitions = new("*,Auto") };
        var left = new ScrollViewer { Content = settings, Padding = new(0, 0, 18, 0) };
        grid.Children.Add(left);
        Grid.SetColumn(result, 2);
        grid.Children.Add(result);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 12, 0, 0), Children = { close, _make } };
        Grid.SetRow(buttons, 1); Grid.SetColumnSpan(buttons, 3);
        grid.Children.Add(buttons);
        Content = new Border { Padding = new Thickness(20), Child = grid };

        CheckCamera();
        Replan();
        if (host.SelectedPaths.Count > 0) SetTools(ShadowBoardMaker.FromShapes(host.SelectedPaths), "from the selected shapes");
    }

    ShadowRecipe? Cut() => _cutWith.SelectedIndex >= 1 && _cutWith.SelectedIndex - 1 < _host.CutRecipes.Count ? _host.CutRecipes[_cutWith.SelectedIndex - 1] : null;
    ShadowRecipe? Engrave() => _engraveWith.SelectedIndex >= 1 && _engraveWith.SelectedIndex - 1 < _host.EngraveRecipes.Count ? _host.EngraveRecipes[_engraveWith.SelectedIndex - 1] : null;

    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    ShadowBoardOptions Options() => new()
    {
        Style = _style.SelectedIndex == 1 ? ShadowBoardStyle.EngravedPockets : ShadowBoardStyle.StackedSheets,
        DepthMm = V(_depth, 9), SheetMm = V(_sheet, 3), EtchMmPerPass = V(_etch, 0.15),
        GapMm = V(_gap, 1.5),
        Notches = _notches.SelectedIndex switch { 1 => NotchPlacement.Front, 2 => NotchPlacement.None, _ => NotchPlacement.BothSides },
        NotchDiameterMm = V(_notch, 22), MarginMm = V(_margin, 15),
        BoardWidthMm = V(_width, 0), BoardHeightMm = V(_height, 0), CornerRadiusMm = V(_corner, 4),
        Labels = _labels.IsChecked == true, LabelSizeMm = V(_labelSize, 5),
        Shadow = _shadow.IsChecked == true, PinDiameterMm = V(_pins, 3), CutOutline = _cutOutline.IsChecked == true,
        CutSettings = Cut()?.Settings,
        EngraveSettings = Engrave()?.Settings,
        PocketSettings = Engrave()?.Settings,
    };

    /// <summary>The tools as named and ticked in the list.</summary>
    List<ShadowTool> Chosen() =>
        [.. _tools.Select((t, i) => (t, i)).Where(x => x.i < _rows.Count && _rows[x.i].Use.IsChecked == true)
            .Select(x => x.t with { Name = _rows[x.i].Name.Text?.Trim() ?? "" })];

    bool CheckCamera()
    {
        string text;
        bool ok = false;
        if (_distance.Value is not { } d) text = "⚠ Enter the distance to the bed the tools lie on.";
        else (ok, text) = _host.CameraStatus((double)d);
        _cameraState.Text = (ok ? "✓ " : "⚠ ") + text.TrimStart('✓', '⚠', ' ');
        _cameraState.Foreground = Res(ok ? "Ok" : "Warn") as IBrush;
        _photo.IsEnabled = ok && !_busyNow;
        return ok;
    }

    async Task FindByCamera()
    {
        if (!CheckCamera() || _distance.Value is not { } d) return;
        var area = _host.WorkArea;
        _busyNow = true;
        _photo.IsEnabled = _make.IsEnabled = false;
        _busy.IsVisible = true;
        _found.Text = "Taking a photo of the bed…";
        try
        {
            var photo = await _host.PhotographBedAsync(area, MmPerPx, (double)d);
            _found.Text = "Outlining the tools…";
            double tol = _tolerance.Value, minArea = V(_minArea, 150);
            var tools = await Task.Run(() =>
            {
                var keep = BackgroundRemoval.Mask(photo, new BackgroundOptions { Tolerance = tol, FromEdges = true, Shrink = 1, Feather = 0 });
                return ShadowBoardMaker.FromMask(keep, photo.Width, photo.Height, area, MmPerPx, minArea);
            });
            SetTools(tools, "on the bed");
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or IOException || e.GetType().Name == "DeviceException")
        {
            _found.Text = $"Couldn't take the photo: {e.Message}";
        }
        finally
        {
            _busyNow = false;
            _busy.IsVisible = false;
            CheckCamera();
        }
    }

    void SetTools(List<ShadowTool> tools, string where)
    {
        _tools = tools;
        _rows.Clear();
        _toolList.Children.Clear();
        foreach (var t in tools)
        {
            var use = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBox { Text = t.Name, HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = "No label" };
            var size = t.Outline.Bounds(0.1);
            var dims = new TextBlock { Text = $"{size.Width:0} × {size.Height:0} mm", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Margin = new(8, 0, 0, 0) };
            ToolTip.SetTip(name, "The label engraved by this tool's pocket. Leave it empty for no label.");
            ToolTip.SetTip(use, "Untick to leave this one out (a scrap, a shadow, something that isn't a tool).");
            use.IsCheckedChanged += (_, _) => Replan();
            name.TextChanged += (_, _) => Replan();
            var row = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
            row.Children.Add(use);
            Grid.SetColumn(name, 1); row.Children.Add(name);
            Grid.SetColumn(dims, 2); row.Children.Add(dims);
            _toolList.Children.Add(row);
            _rows.Add((use, name));
        }
        _found.Text = tools.Count switch
        {
            0 => where == "on the bed"
                ? "No tools found. Check they stand out from the bed (try another tolerance), lie apart from each other and are inside the work area."
                : "The selected shapes have no closed outlines.",
            1 => $"1 tool {where}. Name it for its label:",
            _ => $"{tools.Count} tools {where}, numbered back to front, left to right. Name them for their labels:",
        };
        Replan();
    }

    void Replan()
    {
        bool stacked = _style.SelectedIndex != 1;
        _sheetRow.IsVisible = _shadowRow.IsVisible = _pinsRow.IsVisible = stacked;
        _etchRow.IsVisible = !stacked;
        var tools = Chosen();
        _plan = null;
        if (tools.Count == 0)
        {
            _summary.Text = _tools.Count == 0 ? "Find the tools with the camera, or select their outlines and use those." : "Tick at least one tool.";
            _warnings.Text = "";
            _preview.Show(null, []);
            _make.IsEnabled = false;
            return;
        }
        var o = Options();
        try { _plan = ShadowBoardMaker.Plan(tools, o); }
        catch (InvalidOperationException e) { _summary.Text = e.Message; _make.IsEnabled = false; return; }
        var p = _plan;
        var size = $"{p.Board.Width:0} × {p.Board.Height:0} mm";
        _summary.Text = o.Style == ShadowBoardStyle.StackedSheets
            ? $"Board {size}, {tools.Count} pocket{(tools.Count == 1 ? "" : "s")} {p.DepthMm:0.##} mm deep: cut {p.Sheets} sheet{(p.Sheets == 1 ? "" : "s")} with the pockets and 1 solid base, {(p.Sheets + 1) * o.SheetMm:0.##} mm thick in all."
            : $"Board {size}, {tools.Count} pocket{(tools.Count == 1 ? "" : "s")} engraved {p.Passes} passes of {o.EtchMmPerPass:0.###} mm, about {p.DepthMm:0.##} mm deep." +
              (Engrave()?.EtchMmPerPass is null ? " The depth a pass is a guess without an etch-rate test." : "");
        var warn = p.Warnings.ToList();
        var area = _host.WorkArea;
        if (p.Board.Width > area.Width || p.Board.Height > area.Height)
            warn.Add($"The board is bigger than the work area ({area.Width:0} × {area.Height:0} mm): it won't fit on the bed in one piece.");
        else if (!area.Contains(p.Board))
            warn.Add("The board runs off the work area: move the tools towards the middle of the bed, or move the board after making it.");
        _warnings.Text = string.Join("\n", warn.Select(w => "⚠ " + w));
        _warnings.Foreground = Res("Warn") as IBrush;
        _preview.Show(p, tools);
        _make.IsEnabled = true;
    }

    void Make()
    {
        var tools = Chosen();
        if (tools.Count == 0) return;
        try
        {
            _host.Document.Commit("Shadow board", ShadowBoardMaker.Make(_host.Document.State, tools, Options()));
            Close();
        }
        catch (InvalidOperationException e) { _summary.Text = e.Message; }
    }

    // ---- small helpers (the add-on keeps its own, so it needs nothing private from the program) ----

    static object? Res(string key) => Application.Current?.TryFindResource(key, out var v) == true ? v : null;

    static TextBlock Heading(string text) => new() { Text = text, Classes = { "h" }, Margin = new(0, 6, 0, 0) };

    static Button Wide(string text, bool primary = false)
    {
        var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) b.Classes.Add("primary");
        return b;
    }

    static ComboBox Combo(params string[] items) => new() { ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch };

    static NumericUpDown Number(decimal min, decimal max, decimal step, string format) => new()
    {
        Minimum = min, Maximum = max, Increment = step, FormatString = format,
        ClipValueToMinMax = true, AllowSpin = true, HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    static Control Field(string label, Control editor, string? tip = null)
    {
        var grid = new Grid { ColumnDefinitions = new("130,*") };
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        grid.Children.Add(lbl);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        if (tip is not null) { ToolTip.SetTip(lbl, tip); ToolTip.SetTip(editor, tip); }
        return grid;
    }
}

/// <summary>The board drawn to scale: wood, pockets cut out of it, notches, dowel holes, shadows and labels.</summary>
sealed class BoardPreview : Control
{
    ShadowBoardPlan? _plan;
    IReadOnlyList<ShadowTool> _tools = [];

    public void Show(ShadowBoardPlan? plan, IReadOnlyList<ShadowTool> tools)
    {
        _plan = plan;
        _tools = tools;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (_plan is not { } p)
        {
            var hint = new FormattedText("The board shows here.", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.Gray);
            ctx.DrawText(hint, new Point((size.Width - hint.Width) / 2, (size.Height - hint.Height) / 2));
            return;
        }
        var b = p.Board.Inflate(4);
        double k = Math.Min(size.Width / b.Width, size.Height / b.Height);
        double ox = (size.Width - b.Width * k) / 2 - b.MinX * k, oy = (size.Height - b.Height * k) / 2 - b.MinY * k;
        Point P(Vec2 v) => new(ox + v.X * k, oy + v.Y * k);
        Geometry G(PathGeometry g)
        {
            var sg = new StreamGeometry();
            using var c = sg.Open();
            c.SetFillRule(FillRule.EvenOdd);
            foreach (var f in g.Flatten(0.2))
            {
                if (f.Points.Count < 2) continue;
                c.BeginFigure(P(f.Points[0]), true);
                foreach (var pt in f.Points.Skip(1)) c.LineTo(P(pt));
                c.EndFigure(f.Closed);
            }
            return sg;
        }
        var wood = new SolidColorBrush(Color.FromRgb(0xD9, 0xB3, 0x82));
        var pocket = new SolidColorBrush(Color.FromRgb(0x5A, 0x40, 0x2A));
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(0xE6, 0x39, 0x46)), 1.2);
        var toolPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0x3C, 0xE6, 0x78)), 1);
        ctx.DrawGeometry(wood, edge, G(new PathGeometry([.. p.BoardOutline.Subpaths, .. p.PinHoles.Subpaths])));
        ctx.DrawGeometry(pocket, edge, G(p.Pockets));
        if (!p.Shadows.IsEmpty) ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(120, 0x20, 0x20, 0x20)), null, G(p.Shadows));
        foreach (var t in _tools) ctx.DrawGeometry(null, toolPen, G(t.Outline));
        foreach (var l in p.Labels)
        {
            var ft = new FormattedText(l.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, Math.Max(6, l.Box.Height * k), l.Fits ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)) : Brushes.OrangeRed);
            var c = P(l.Centre);
            ctx.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
        }
    }
}
