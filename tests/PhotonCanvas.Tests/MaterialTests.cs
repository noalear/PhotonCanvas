using System.IO.Compression;
using System.Text;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Tests;

public sealed class MaterialTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "photoncanvas-materials-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    static Material Basswood() => new()
    {
        Name = "Basswood",
        Category = "Wood",
        ThicknessMm = 3,
        Recipes =
        [
            new Recipe { Purpose = RecipePurpose.Cut, Title = "Clean cut", Rating = 5, Tested = true,
                Settings = new ProcessSettings { Mode = LayerMode.Cut, Laser = LaserSettings.Blue(70, 40).WithPasses(8), KerfMm = 0.12 } },
            new Recipe { Purpose = RecipePurpose.Engrave, Title = "Dark fill", Rating = 4,
                Settings = new ProcessSettings { Mode = LayerMode.Fill, Laser = LaserSettings.Blue(30, 400), LinesPerCm = 120 } },
        ],
    };

    [Fact]
    public void SavesAndLoads()
    {
        var lib = new MaterialLibrary(_dir);
        lib.Upsert(Basswood());
        lib.Save();
        var back = MaterialLibrary.Load(_dir);
        var m = Assert.Single(back.Materials);
        Assert.Equal("Basswood 3 mm", m.Label);
        Assert.Equal(2, m.Recipes.Count);
        Assert.Equal(8, m.Recipes[0].Settings.Laser.Passes);
        Assert.Equal(0.12, m.Recipes[0].Settings.KerfMm);
        Assert.Equal(LayerMode.Fill, m.Recipes[1].Settings.Mode);
        Assert.Empty(MaterialLibrary.Load(Path.Combine(_dir, "nothing-here")).Materials);
    }

    [Fact]
    public void DamagedFileIsSetAsideNotFatal()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "library.json"), "{ not json");
        Assert.Empty(MaterialLibrary.Load(_dir).Materials);
        Assert.Contains(Directory.GetFiles(_dir), f => f.Contains("damaged"));
    }

    [Fact]
    public void SettingsGoOntoALayerAndBack()
    {
        var layer = Layer.Default(0) with { Name = "Mine", Color = 0x123456 };
        var r = Basswood().Recipes[0];
        var applied = r.Settings.ApplyTo(layer);
        Assert.Equal(("Mine", 0x123456u), (applied.Name, applied.Color)); // only processing settings change
        Assert.Equal(r.Settings, ProcessSettings.From(applied));
        Assert.Equal("cut · diode 70% · 40 mm/s ×8", r.Settings.Summary());
    }

    [Fact]
    public void SharesWithPhotosAndMergesOnImport()
    {
        var mine = new MaterialLibrary(Path.Combine(_dir, "me"));
        var bass = Basswood();
        var photo = mine.SavePhoto(bass.Recipes[0].Id, [0xFF, 0xD8, 0xFF, 1, 2, 3]);
        bass = bass with { Recipes = bass.Recipes.SetItem(0, bass.Recipes[0] with { Photo = photo }) };
        mine.Upsert(bass);
        using var file = new MemoryStream();
        mine.Export(file, mine.Materials, "Noa");

        // A friend imports it: one new material, two recipes, photo included, tested by the author.
        var friend = new MaterialLibrary(Path.Combine(_dir, "friend"));
        file.Position = 0;
        var r = friend.Import(file, "shared.pcmat");
        Assert.Equal((1, 2, "Noa"), (r.NewMaterials, r.NewRecipes, r.From));
        var got = Assert.Single(friend.Materials);
        Assert.Equal("Noa", got.Recipes[0].TestedBy);
        Assert.Equal("", got.Recipes[1].TestedBy); // untested recipes don't claim a tester
        Assert.NotNull(friend.PhotoPath(got.Recipes[0].Photo));

        // Importing again adds nothing; a new recipe on the same material is merged in.
        file.Position = 0;
        Assert.Equal(0, friend.Import(file, "again").NewRecipes);
        var extra = bass with { Recipes = bass.Recipes.Add(new Recipe { Purpose = RecipePurpose.Score, Title = "Light line" }) };
        using var file2 = new MemoryStream();
        mine.Upsert(extra);
        mine.Export(file2, mine.Materials, null);
        file2.Position = 0;
        var r2 = friend.Import(file2, "update.pcmat");
        Assert.Equal((0, 1, "update.pcmat"), (r2.NewMaterials, r2.NewRecipes, r2.From));
        Assert.Equal(3, Assert.Single(friend.Materials).Recipes.Count);
    }

    [Fact]
    public void XToolsOwnSettingsAreNeverExported()
    {
        var mine = new MaterialLibrary(Path.Combine(_dir, "xt"));
        var bass = Basswood();
        var xtool = new Recipe { Purpose = RecipePurpose.Cut, Title = "Cut (xTool)", Tested = true, TestedBy = MaterialLibrary.XToolTester };
        mine.Upsert(bass with { Recipes = bass.Recipes.Add(xtool) });
        mine.Upsert(new Material { Name = "Walnut", ThicknessMm = 3, Recipes = [xtool with { Id = Guid.NewGuid() }] });
        using var file = new MemoryStream();
        var done = mine.Export(file, mine.Materials, "Noa");
        Assert.Equal((1, 2, 2), (done.Materials, done.Recipes, done.XToolLeftOut));

        var friend = new MaterialLibrary(Path.Combine(_dir, "xt-friend"));
        file.Position = 0;
        friend.Import(file, "shared.pcmat");
        var got = Assert.Single(friend.Materials);
        Assert.DoesNotContain(got.Recipes, MaterialLibrary.IsXToolPreset);
        Assert.Equal(2, got.Recipes.Count);
    }

    [Fact]
    public void ImportDoesNotTrustTheFile()
    {
        // Out-of-range settings, a path-escaping photo name, a nameless material.
        var bad = new Material
        {
            Name = "Sketchy",
            Recipes = [new Recipe { Title = "Too hot", Photo = "../../evil.jpg",
                Settings = new ProcessSettings { Laser = LaserSettings.Blue(500, 1e9) with { Passes = -3 }, LinesPerCm = 1e6 } }],
        };
        using var file = new MemoryStream();
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            var src = new MaterialLibrary(Path.Combine(_dir, "src"));
            src.Upsert(bad);
            src.Upsert(new Material { Name = "" });
            using var tmp = new MemoryStream();
            src.Export(tmp, src.Materials, "x");
            tmp.Position = 0;
            using var inner = new ZipArchive(tmp);
            using (var w = zip.CreateEntry("library.json").Open()) inner.GetEntry("library.json")!.Open().CopyTo(w);
            using (var w = new StreamWriter(zip.CreateEntry("photos/../../evil.jpg").Open())) w.Write("x");
        }
        file.Position = 0;
        var lib = new MaterialLibrary(Path.Combine(_dir, "dst"));
        var result = lib.Import(file, "bad.pcmat");
        Assert.Equal(1, result.NewMaterials);
        Assert.Equal(1, result.Skipped); // the nameless one
        var s = lib.Materials.Single().Recipes.Single().Settings;
        Assert.Equal(100, s.Laser.Power);
        Assert.Equal(MachineProfile.F2Ultra.MaxSpeed, s.Laser.Speed);
        Assert.Equal(1, s.Laser.Passes);
        Assert.Equal(FillBlock.MaxLinesPerCm, s.LinesPerCm);
        Assert.Null(lib.Materials.Single().Recipes.Single().Photo);
        Assert.False(File.Exists(Path.Combine(_dir, "evil.jpg")));

        Assert.Throws<FormatException>(() =>
        {
            using var notZip = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
            lib.Import(notZip, "x");
        });
    }

    [Fact]
    public void OldPresetsBecomeMaterialsOnce()
    {
        var lib = new MaterialLibrary(_dir);
        var presets = new[] { ("3 mm ply cut", LayerMode.Cut, LaserSettings.Blue(80, 30), 100.0), ("Logo fill", LayerMode.Fill, LaserSettings.Blue(25, 300), 140.0) };
        Assert.Equal(2, lib.AdoptPresets(presets));
        Assert.Equal(0, lib.AdoptPresets(presets));
        var fill = lib.Materials.Single(m => m.Name == "Logo fill").Recipes.Single();
        Assert.Equal((RecipePurpose.Engrave, 140.0), (fill.Purpose, fill.Settings.LinesPerCm));
    }

    [Fact]
    public void CropsARecipePhotoFromTheWorkAreaPicture()
    {
        // 100 × 100 mm at 0.5 mm/px; a red square at 20–30 mm.
        var rgba = new byte[200 * 200 * 4];
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 200; x++)
            {
                int o = (y * 200 + x) * 4;
                bool red = x is >= 40 and < 60 && y is >= 40 and < 60;
                rgba[o] = red ? (byte)220 : (byte)40; rgba[o + 1] = 40; rgba[o + 2] = 40; rgba[o + 3] = 255;
            }
        var jpg = MaterialLibrary.CropPhoto(new RasterImage(200, 200, rgba), new Rect2(0, 0, 100, 100), 0.5, new Rect2(20, 20, 30, 30))!;
        var back = RasterImage.Decode(jpg);
        Assert.Equal((20, 20), (back.Width, back.Height));
        Assert.True(back.Rgba[(10 * 20 + 10) * 4] > 180); // red in the middle
        Assert.Null(MaterialLibrary.CropPhoto(new RasterImage(200, 200, rgba), new Rect2(0, 0, 100, 100), 0.5, new Rect2(150, 150, 160, 160)));
    }

    [Fact]
    public void LayerMaterialIsSavedWithTheDesign()
    {
        var s = DocState.New();
        var mref = new MaterialRef(Guid.NewGuid(), "Basswood 3 mm", Guid.NewGuid(), "Clean cut");
        s = s with { Layers = [s.Layers[0] with { Material = mref }] };
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.Equal(mref, back.Layers[0].Material);
        Assert.Null(DocFile.Deserialize(DocFile.Serialize(DocState.New())).Layers[0].Material);
    }
}
