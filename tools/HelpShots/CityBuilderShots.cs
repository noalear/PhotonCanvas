using System.Reflection;
using Avalonia.Headless;
using Avalonia.Controls;
using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using SkiaSharp;

/// <summary>The city builder's pictures (in its own help/images): the sample village on the plan and in 3D, a building, several things and a road picked, the town's buildings by district, a town made for you, its Cut tab, a part of it saved as a battle map picture, its ground cut as tiles, and the sample battle map read into a town.</summary>
static class CityBuilderShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "CityBuilder", "help", "images");
        Directory.CreateDirectory(folder);
        // A scratch data folder, so the pictures don't depend on the last map made.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-citybuilder");

        void Shot(string name, CityMap map, int left, int view, Func<CityWindow, Task>? also = null, Action<CityWindow>? after = null)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), map.ToJson());
            var win = new CityWindow(doc, machine, lib, data);
            win.Show(owner);
            settle(300);
            if (also is not null)
            {
                var t = also(win);
                for (int i = 0; i < 1200 && !t.IsCompleted; i++) settle(50);
            }
            foreach (var tc in win.GetLogicalDescendantsOf<TabControl>())
            {
                if (tc.Items.Count == 6 && tc.Items[0] is TabItem { Header: "Map" }) tc.SelectedIndex = left;
                if (tc.Items.Count == 2 && tc.Items[0] is TabItem { Header: "Plan" }) tc.SelectedIndex = view;
            }
            settle(1500);
            if (after is not null)
            {
                after(win);
                settle(1200);
            }
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"citybuilder: {name}.png");
            win.Close();
            settle(200);
        }

        var village = CityWindow.Sample();
        Shot("window-citybuilder-plan", village, 2, 0);
        Shot("window-citybuilder-3d", village, 0, 1);
        Shot("window-citybuilder-picked", village, 4, 0, w =>
        {
            F<CityPlan>(w, "_plan").Select(village.Buildings[0].Id);
            return Task.CompletedTask;
        });
        Shot("window-citybuilder-several", village, 4, 0, w =>
        {
            // A box round the tavern, the well, the cart and the square's crates and lamp.
            F<CityPlan>(w, "_plan").SetPick(CityEdit.Within(village, 9.5, 7.5, 22, 16.5));
            return Task.CompletedTask;
        });
        Shot("window-citybuilder-town", village, 3, 0, w =>
        {
            F<CheckBox>(w, "_byDistrict").IsChecked = true;
            return Task.CompletedTask;
        });
        // A town made for you: a walled crossroads town with a river.
        async Task MakeOne(CityWindow w)
        {
            F<NumericUpDown>(w, "_across").Value = 64;
            F<NumericUpDown>(w, "_down").Value = 48;
            F<ComboBox>(w, "_tLayout").SelectedIndex = 0;
            F<CheckBox>(w, "_tWalls").IsChecked = true;
            F<CheckBox>(w, "_tRiver").IsChecked = true;
            w.GetType().GetField("_seed", Any)!.SetValue(w, 7);
            await (Task)w.GetType().GetMethod("MakeTown", Any)!.Invoke(w, null)!;
            F<Button>(w, "_tMake").BringIntoView();
        }
        Shot("window-citybuilder-made", village, 0, 0, MakeOne);
        Shot("window-citybuilder-made-3d", village, 0, 1, MakeOne);
        Shot("window-citybuilder-cut", village, 5, 0, MakeOne);
        // A lane picked in the made town: its handles on the plan, its name, surface and width on the Picked tab.
        Shot("window-citybuilder-road", village, 4, 0, MakeOne, w =>
        {
            var plan = F<CityPlan>(w, "_plan");
            var lane = plan.Map.Roads.Where(r => r.Width == 2).OrderByDescending(r => r.Length).First();
            plan.SetPick(CityPick.Of(lane));
            // Its name in the middle, the bridge beside it.
            var (_, at, _) = CityPlan.RoadNames(plan.Map).First(n => n.Road.Id == lane.Id);
            plan.CentreOn(at.X + 3, at.Y);
        });
        // Closer in on the market square in 3D: its stage, stalls, bar and trough, and the guard towers on the wall beyond.
        Shot("window-citybuilder-square-3d", village, 0, 1, MakeOne, w =>
        {
            var view = F<CityView>(w, "_view");
            view.Turn(-400, -5);
            view.Zoom(3.4, new Avalonia.Point(view.Bounds.Width / 2, view.Bounds.Height / 2));
        });
        // A town like it saved as a battle map picture (a part of it), and the ground by the river cut as tiles.
        var (town, _) = TownMaker.Make(new TownOptions { Name = "New town", Across = 64, Down = 48, Layout = TownLayout.Crossroads, Walls = true, River = true, Seed = 7, Theme = "Medieval" });
        File.WriteAllBytes(Path.Combine(folder, "citybuilder-picture.png"), TownPicture.Encode(town, new TownPictureOptions { PxPerSquare = 36, Box = (18, 16, 54, 38) }, "town.png"));
        Console.WriteLine("citybuilder: citybuilder-picture.png");
        var (_, ground) = GroundBoards.Make(town, new GroundOptions { TileSquares = 4, Box = (40, 18, 56, 30) });
        File.WriteAllBytes(Path.Combine(folder, "citybuilder-ground.png"), TilesPicture(ground.Board, 3, 1.5));
        Console.WriteLine("citybuilder: citybuilder-ground.png");
        Shot("window-citybuilder-battlemap", CityMap.Empty(40, 28), 1, 0, async w =>
        {
            await w.UsePicture(SampleBattleMap.Make(), null);
            await w.ReadPicture();
        });
        Shot("window-citybuilder-battlemap-3d", CityMap.Empty(40, 28), 1, 1, async w =>
        {
            await w.UsePicture(SampleBattleMap.Make(), null);
            await w.ReadPicture();
        });
    }

    /// <summary>Floor tiles drawn as cut: light wood, the etching darker, scored lines dark, each a little apart from the next.</summary>
    static byte[] TilesPicture(List<PhotonCanvas.Addon.FloorTiles.BoardSpot> board, double pxPerMm, double gapMm)
    {
        double tw = board.Max(b => b.W), th = board.Max(b => b.H);
        int Col(PhotonCanvas.Addon.FloorTiles.BoardSpot b) => (int)Math.Round(b.X / tw);
        int Row(PhotonCanvas.Addon.FloorTiles.BoardSpot b) => (int)Math.Round(b.Y / th);
        double maxX = board.Max(b => b.X + b.W + Col(b) * gapMm), maxY = board.Max(b => b.Y + b.H + Row(b) * gapMm), margin = 6;
        int W = (int)Math.Ceiling((maxX + 2 * margin) * pxPerMm), H = (int)Math.Ceiling((maxY + 2 * margin) * pxPerMm);
        using var bmp = new SKBitmap(W, H);
        using var c = new SKCanvas(bmp);
        c.Clear(new SKColor(0x2b, 0x2d, 0x31));
        SKPoint At(double x, double y) => new((float)(x * pxPerMm), (float)(H - y * pxPerMm));
        SKPath Area(Clipper2Lib.PathsD paths, double ox, double oy)
        {
            var p = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var path in paths.Where(q => q.Count > 2))
            {
                p.MoveTo(At(ox + path[0].x, oy + path[0].y));
                for (int i = 1; i < path.Count; i++) p.LineTo(At(ox + path[i].x, oy + path[i].y));
                p.Close();
            }
            return p;
        }
        using var wood = new SKPaint { Color = new SKColor(0xe2, 0xc4, 0x96), IsAntialias = true };
        using var burnt = new SKPaint { Color = new SKColor(0x8a, 0x5a, 0x2e), IsAntialias = true };
        using var line = new SKPaint { Color = new SKColor(0x5a, 0x38, 0x1c), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(1, 0.35 * pxPerMm), StrokeCap = SKStrokeCap.Round };
        using var edge = new SKPaint { Color = new SKColor(0x3c, 0x24, 0x10), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 110), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(1.5 * pxPerMm)) };
        foreach (var b in board)
        {
            double ox = margin + b.X + Col(b) * gapMm, oy = margin + b.Y + Row(b) * gapMm;
            using var cut = Area(b.Tile.Cut, ox, oy);
            c.Save();
            c.Translate((float)(1.2 * pxPerMm), (float)(1.2 * pxPerMm));
            c.DrawPath(cut, shadow);
            c.Restore();
            c.DrawPath(cut, wood);
            using var etched = Area(b.Tile.Etched, ox, oy);
            c.DrawPath(etched, burnt);
            using var lines = new SKPath();
            foreach (var l in b.Tile.Lines.Where(q => q.Count > 1))
            {
                lines.MoveTo(At(ox + l[0].x, oy + l[0].y));
                for (int i = 1; i < l.Count; i++) lines.LineTo(At(ox + l[i].x, oy + l[i].y));
            }
            c.DrawPath(lines, line);
            c.DrawPath(cut, edge);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static IEnumerable<T> GetLogicalDescendantsOf<T>(this Control c) => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(c).OfType<T>();
}
