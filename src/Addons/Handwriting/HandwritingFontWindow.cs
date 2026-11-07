using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core.Imaging;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Handwriting;

/// <summary>
/// Make a handwriting font: print the sheet, read a photo of it filled in, check each letter,
/// save. Returns the saved font's name (null when closed without saving).
/// </summary>
public sealed class HandwritingFontWindow : Window
{
    readonly IAddonHost _host;
    readonly FontStore _store;
    readonly ComboBox _paper = new() { ItemsSource = new[] { "A4", "US Letter" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Slider _ink = new() { Minimum = 35, Maximum = 85, Value = 62 };
    readonly Slider _smooth = new() { Minimum = 5, Maximum = 60, Value = 18 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Text = "No sheet read yet." };
    readonly TextBox _name = new() { Watermark = "Name of the font" };
    readonly RadioButton _extra = new() { Content = "Add as extra versions of its letters", IsChecked = true, GroupName = "hwmerge" };
    readonly RadioButton _replace = new() { Content = "Replace the letters it has", GroupName = "hwmerge" };
    readonly Control _mergeRow;
    readonly Button _save = new() { Content = "Save font", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _bed = Button("Photograph it on the bed", tip: "Lay the filled-in sheet flat in the machine: this takes an overview camera photo (nothing moves, the laser stays off)");
    readonly Button _picture = Button("Use the selected picture", tip: "A photo of the sheet already in the design (Import → Picture)");
    readonly WrapPanel _tiles = new() { Orientation = Orientation.Horizontal };
    readonly DispatcherTimer _later = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly HashSet<int> _skipped = [];
    RasterImage? _photo;
    SheetReading? _reading;
    int _readCount;

    public HandwritingFontWindow(IAddonHost host, FontStore store, string? current)
    {
        _host = host;
        _store = store;
        Title = "Make a handwriting font";
        Width = 1080;
        Height = 720;
        MinWidth = 800;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _name.Text = current ?? "My handwriting";
        _mergeRow = new StackPanel { Spacing = 4, Children = { Hint("There's a font with this name already:"), _extra, _replace } };

        var pdf = Button("Save the sheet as PDF…", tip: "A page with a box for each letter, number and sign, to print and fill in");
        pdf.Click += async (_, _) => await _host.Guard(SavePdf);
        var open = Button("Open a photo or scan…", tip: "A phone photo or a scan of the filled-in sheet (JPEG, PNG…)");
        open.Click += async (_, _) => await _host.Guard(OpenPhoto);
        _picture.Click += (_, _) => { if (_host.SelectedPicture is { } im) Use(im.Source.Pixels); };
        _picture.IsEnabled = _host.SelectedPicture is not null;
        _bed.IsEnabled = _host.IsConnected;
        _bed.Click += async (_, _) => await _host.Guard(BedPhoto);
        _ink.ValueChanged += (_, _) => Later();
        _smooth.ValueChanged += (_, _) => Later();
        _later.Tick += (_, _) => { _later.Stop(); _ = Read(); };
        _name.TextChanged += (_, _) => Merge();
        _save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => Close(null);

        var side = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Explained("1. Print the sheet", "A box for each capital, small letter, number and sign, with lines to write on.",
                    Field("Paper", _paper), pdf,
                    Hint("Print at actual size (100%). Write with a dark pen, one character per box, standing on the solid line.")),
                Explained("2. Read the filled-in sheet", "Take the photo straight on if you can, in even light, with all four black corner squares in it.",
                    open, _picture, _bed,
                    Field("Ink threshold", _ink, tip: "How dark counts as ink, against the paper around it. Lower if the box lines show up in the letters; higher if thin or pale strokes go missing."),
                    Field("Smoothing", _smooth, tip: "How much the traced lines are straightened out. More gives smoother curves, less keeps small wiggles."),
                    _status),
                Explained("3. Save the font", "Click any letter on the right to leave it out (click again to keep it).",
                    Labelled("Font name", _name), _mergeRow),
            },
        };
        var bottom = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Margin = new(0, 10, 0, 0), Children = { _save, cancel.With(c => Grid.SetColumn(c, 1)) } };
        var left = new DockPanel { Width = 360, Margin = new(0, 0, 16, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        left.Children.Add(bottom);
        left.Children.Add(new ScrollViewer { Content = side.With(p => p.Margin = new(0, 0, 14, 0)), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var body = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(left, Dock.Left);
        body.Children.Add(left);
        body.Children.Add(new Border
        {
            Background = Brush("Bg0"), CornerRadius = new(8), Padding = new(8),
            Child = new ScrollViewer { Content = _tiles, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        });
        Content = body;
        ShowTiles();
        Merge();
    }

    void Later() { if (_photo is null) return; _later.Stop(); _later.Start(); }

    void Merge()
    {
        _mergeRow.IsVisible = _store.Find(_name.Text?.Trim() ?? "") is not null;
        _save.IsEnabled = _reading is { Problem: null } r && r.Boxes.Where((b, i) => b.Glyph is not null && !_skipped.Contains(i)).Any() && !string.IsNullOrWhiteSpace(_name.Text);
    }

    async Task SavePdf()
    {
        var paper = _paper.SelectedIndex == 1 ? Paper.Letter : Paper.A4;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the handwriting sheet",
            SuggestedFileName = "Handwriting sheet.pdf",
            DefaultExtension = "pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var name = _name.Text?.Trim();
        await File.WriteAllBytesAsync(path, FontSheet.Pdf(paper, string.IsNullOrEmpty(name) ? null : name));
        _host.Status($"Saved the handwriting sheet ({(paper == Paper.Letter ? "US Letter" : "A4")}) to {Path.GetFileName(path)}. Print it at actual size.");
    }

    async Task OpenPhoto()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Photo or scan of the filled-in sheet",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        RasterImage img;
        try { img = await Task.Run(() => RasterImage.Decode(File.ReadAllBytes(path))); }
        catch (FormatException e) { await Dialogs.Message(this, "Open a photo", e.Message); return; }
        Use(img);
    }

    async Task BedPhoto()
    {
        _status.Text = "Taking a camera photo of the bed…";
        var photo = await _host.TakePhotoAsync();
        if (photo is null) { _status.Text = "No camera photo: is the laser connected?"; return; }
        Use(photo.Image);
    }

    void Use(RasterImage img)
    {
        _photo = img;
        _skipped.Clear();
        _ = Read();
    }

    async Task Read()
    {
        if (_photo is not { } photo) return;
        int n = ++_readCount;
        _status.Text = "Reading the sheet…";
        var o = new SheetOptions(_ink.Value / 100, _smooth.Value / 100);
        var reading = await Task.Run(() => SheetReader.Read(photo, o));
        if (n != _readCount) return;
        _reading = reading;
        _status.Text = reading.Problem ?? $"Found the sheet: {reading.Written} of {reading.Boxes.Count} boxes have writing in them.";
        ShowTiles();
        Merge();
    }

    void ShowTiles()
    {
        _tiles.Children.Clear();
        if (_reading is not { Problem: null } r)
        {
            _tiles.Children.Add(new TextBlock
            {
                Margin = new(16), TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Classes = { "muted" },
                Text = "The letters read from your sheet show here, each in its box: the photo in grey, the lines the laser will follow in colour.",
            });
            return;
        }
        for (int i = 0; i < r.Boxes.Count; i++)
        {
            int index = i;
            var tile = new GlyphTile(r.Boxes[i], _skipped.Contains(i));
            if (r.Boxes[i].Glyph is not null)
            {
                tile.Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(tile, "Click to leave this letter out (or keep it again)");
                tile.PointerPressed += (_, _) =>
                {
                    if (!_skipped.Remove(index)) _skipped.Add(index);
                    tile.Skipped = _skipped.Contains(index);
                    Merge();
                };
            }
            _tiles.Children.Add(tile);
        }
    }

    void Save()
    {
        if (_reading is not { Problem: null } r) return;
        string name = _name.Text?.Trim() ?? "";
        if (name.Length == 0) return;
        var letters = r.Boxes.Select((b, i) => (b, i)).Where(x => x.b.Glyph is not null && !_skipped.Contains(x.i)).Select(x => (x.b.Char, x.b.Glyph!)).ToList();
        if (letters.Count == 0) return;
        var font = (_store.Find(name) ?? new HandFont { Name = name }).Renamed(name).With(letters, _extra.IsChecked == true);
        _store.Save(font);
        _host.Status($"Saved the handwriting font \"{name}\": {font.Glyphs.Count} characters.");
        Close(name);
    }
}

/// <summary>One box of the sheet: the photo faintly, the traced lines over it, its character.</summary>
public sealed class GlyphTile : Control
{
    readonly SheetBox _box;
    readonly WriteableBitmap _image;
    bool _skipped;

    public GlyphTile(SheetBox box, bool skipped)
    {
        _box = box;
        _skipped = skipped;
        Width = 76;
        Height = 100;
        Margin = new(3);
        _image = new WriteableBitmap(new PixelSize(box.W, box.H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = _image.Lock();
        var row = new byte[box.W * 4];
        for (int y = 0; y < box.H; y++)
        {
            for (int x = 0; x < box.W; x++)
            {
                // Faint: lifted towards white so the traced lines stand out.
                byte g = (byte)(150 + box.Gray[y * box.W + x] * 105 / 255);
                row[x * 4] = g; row[x * 4 + 1] = g; row[x * 4 + 2] = g; row[x * 4 + 3] = 255;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
        }
    }

    public bool Skipped { get => _skipped; set { _skipped = value; InvalidateVisual(); } }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        ctx.FillRectangle(Brushes.White, r);
        // The box as read (it starts SheetReader.Inset inside the printed box).
        double s = Math.Min(r.Width / _box.W, (r.Height - 16) / _box.H);
        double ox = (r.Width - _box.W * s) / 2, oy = 16;
        var img = new Rect(ox, oy, _box.W * s, _box.H * s);
        ctx.DrawImage(_image, img);
        // Mm on the sheet from the read area's corner → tile pixels.
        double px = s * SheetReader.Ppm;
        double Base(double fromBoxTop) => oy + (fromBoxTop - SheetReader.Inset) * px;
        var guide = new Pen(new SolidColorBrush(Color.Parse("#5590D0"), 0.35), 1);
        ctx.DrawLine(guide, new Point(ox, Base(FontSheet.Baseline)), new Point(ox + _box.W * s, Base(FontSheet.Baseline)));
        if (_box.Glyph is { } g)
        {
            // The glyph was moved so its ink starts at x = 0: back to where it was written.
            double gx = ox + (_box.Left * FontSheet.CapHeight - SheetReader.Inset) * px;
            var sg = new StreamGeometry();
            using (var c = sg.Open())
                foreach (var st in g.Strokes)
                {
                    var p0 = st.Points[0];
                    c.BeginFigure(new Point(gx + p0.X * FontSheet.CapHeight * px, Base(FontSheet.Baseline) + p0.Y * FontSheet.CapHeight * px), false);
                    foreach (var p in st.Points.Skip(1)) c.LineTo(new Point(gx + p.X * FontSheet.CapHeight * px, Base(FontSheet.Baseline) + p.Y * FontSheet.CapHeight * px));
                    c.EndFigure(st.Closed);
                }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(_skipped ? Color.Parse("#999999") : Color.Parse("#C0392B")), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), sg);
        }
        var label = new FormattedText(_box.Char, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.Black);
        ctx.DrawText(label, new Point(4, 1));
        if (_box.Glyph is null || _skipped)
        {
            var t = new FormattedText(_box.Glyph is null ? "empty" : "left out", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.Gray);
            ctx.DrawText(t, new Point(r.Width - t.Width - 4, 2));
        }
        if (_skipped) ctx.FillRectangle(new SolidColorBrush(Colors.White, 0.45), img);
        ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#CCCCCC")), 1), r.Deflate(0.5));
    }
}
