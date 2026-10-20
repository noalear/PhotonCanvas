using System.Runtime.CompilerServices;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Tests;

static class TestData
{
    /// <summary>Tests keep their settings, keys and libraries in a throwaway folder, never the real one.</summary>
    [ModuleInitializer]
    internal static void UseScratchData() =>
        Environment.SetEnvironmentVariable(Core.AppIdentity.DataDirVariable, Path.Combine(Path.GetTempPath(), $"pc-data-{Environment.ProcessId}"));
}

public class DataMergeTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"pc-merge-{Guid.NewGuid():N}");
    string Here => Path.Combine(_root, "here");
    string Earlier => Path.Combine(_root, "earlier");

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    static Recipe R(string title, DateTime updated) => new() { Title = title, Updated = updated };

    static void SaveLibrary(string dataDir, params Material[] materials)
    {
        var lib = new MaterialLibrary(Path.Combine(dataDir, "materials"));
        foreach (var m in materials) lib.Upsert(m);
        lib.Save();
    }

    static MaterialLibrary Library(string dataDir) => MaterialLibrary.Load(Path.Combine(dataDir, "materials"));

    [Fact]
    public void LibrariesAreCombinedRecipeByRecipe()
    {
        var old = new DateTime(2026, 10, 1);
        var shared = R("Cut", old);
        var basswood = new Material { Name = "Basswood" };
        // Here: the shared recipe changed later, plus a recipe only here, plus a material only here.
        SaveLibrary(Here, basswood with { Recipes = [shared with { Title = "Cut, retuned", Updated = old.AddDays(5) }, R("Score", old)] }, new Material { Name = "Acrylic" });
        // Earlier: the shared recipe's older copy, a recipe only there, and a material only there.
        SaveLibrary(Earlier, basswood with { Recipes = [shared, R("Pictures, matched", old.AddDays(2))] }, new Material { Name = "Leather" });

        Assert.Equal(1, DataMerge.Run(Here, [Earlier]));

        var lib = Library(Here);
        Assert.Equal(["Acrylic", "Basswood", "Leather"], lib.Materials.Select(m => m.Name).Order());
        var titles = lib.Materials.Single(m => m.Name == "Basswood").Recipes.Select(r => r.Title).Order();
        Assert.Equal(["Cut, retuned", "Pictures, matched", "Score"], titles);
    }

    [Fact]
    public void TheSameMaterialMadeTwiceBecomesOne()
    {
        // Each place made its own Basswood: different ids, same name and thickness.
        var etch = R("Etch", new DateTime(2026, 10, 17));
        SaveLibrary(Here, new Material { Name = "Basswood", ThicknessMm = 2.75, Recipes = [etch, R("Pictures, matched", DateTime.Now)] });
        SaveLibrary(Earlier, new Material { Name = "Basswood", ThicknessMm = 2.75, Recipes = [etch with { Id = Guid.NewGuid() }, R("Cuts", DateTime.Now)] });
        DataMerge.Run(Here, [Earlier]);
        var m = Assert.Single(Library(Here).Materials);
        Assert.Equal(["Cuts", "Etch", "Pictures, matched"], m.Recipes.Select(r => r.Title).Order());
    }

    [Fact]
    public void CopiesLeftByAnEarlierMergeAreJoined()
    {
        SaveLibrary(Here, new Material { Name = "Basswood", ThicknessMm = 2.75, Recipes = [R("A", DateTime.Now)] }, new Material { Name = "Basswood", ThicknessMm = 2.75, Recipes = [R("B", DateTime.Now)] });
        SaveLibrary(Earlier, new Material { Name = "Steel" });
        DataMerge.Run(Here, [Earlier]);
        Assert.Equal(["A", "B"], Library(Here).Materials.Single(m => m.Name == "Basswood").Recipes.Select(r => r.Title).Order());
    }

    [Fact]
    public void ANewerCopyThereWinsOverAnOlderOneHere()
    {
        var shared = R("Cut", new DateTime(2026, 10, 1));
        var m = new Material { Name = "Basswood" };
        SaveLibrary(Here, m with { Recipes = [shared] });
        SaveLibrary(Earlier, m with { Recipes = [shared with { Title = "Cut, slower", Updated = new DateTime(2026, 10, 9) }] });
        DataMerge.Run(Here, [Earlier]);
        Assert.Equal("Cut, slower", Assert.Single(Assert.Single(Library(Here).Materials).Recipes).Title);
    }

    [Fact]
    public void ARecipeDeletedHereDoesNotComeBack()
    {
        var m = new Material { Name = "Basswood" };
        SaveLibrary(Earlier, m with { Recipes = [R("Cut", DateTime.Now)] });
        DataMerge.Run(Here, [Earlier]);
        Assert.Single(Assert.Single(Library(Here).Materials).Recipes);

        var lib = Library(Here);
        lib.Upsert(lib.Materials[0] with { Recipes = [] });
        lib.Save();
        Assert.Equal(0, DataMerge.Run(Here, [Earlier]));
        Assert.Empty(Assert.Single(Library(Here).Materials).Recipes);
    }

    [Fact]
    public void OtherFilesAreCopiedWhenMissingOrNewer()
    {
        Directory.CreateDirectory(Path.Combine(Earlier, "logs"));
        Directory.CreateDirectory(Here);
        File.WriteAllText(Path.Combine(Earlier, "logs", "2026-10-18.log"), "old log");
        File.WriteAllText(Path.Combine(Earlier, "camera.json"), "older camera");
        File.SetLastWriteTimeUtc(Path.Combine(Earlier, "camera.json"), new DateTime(2026, 10, 1));
        File.WriteAllText(Path.Combine(Here, "camera.json"), "newer camera");
        File.WriteAllText(Path.Combine(Earlier, "keys.json"), "newer keys");
        File.WriteAllText(Path.Combine(Here, "keys.json"), "older keys");
        File.SetLastWriteTimeUtc(Path.Combine(Here, "keys.json"), new DateTime(2026, 10, 1));

        DataMerge.Run(Here, [Earlier]);

        Assert.Equal("old log", File.ReadAllText(Path.Combine(Here, "logs", "2026-10-18.log")));
        Assert.Equal("newer camera", File.ReadAllText(Path.Combine(Here, "camera.json")));
        Assert.Equal("newer keys", File.ReadAllText(Path.Combine(Here, "keys.json")));
        Assert.True(File.Exists(Path.Combine(Earlier, "keys.json"))); // the earlier folder is left alone
    }

    [Fact]
    public void JobHistoryKeepsEveryJobOnce()
    {
        Directory.CreateDirectory(Here);
        Directory.CreateDirectory(Earlier);
        File.WriteAllLines(Path.Combine(Here, "job-history.jsonl"), ["{\"When\":\"2026-10-17\"}", "{\"When\":\"2026-10-19\"}"]);
        File.WriteAllLines(Path.Combine(Earlier, "job-history.jsonl"), ["{\"When\":\"2026-10-17\"}", "{\"When\":\"2026-10-18\"}"]);
        DataMerge.Run(Here, [Earlier]);
        Assert.Equal(["{\"When\":\"2026-10-17\"}", "{\"When\":\"2026-10-18\"}", "{\"When\":\"2026-10-19\"}"], File.ReadAllLines(Path.Combine(Here, "job-history.jsonl")));
    }

    [Fact]
    public void TheDataFolderIsNotMergedIntoItself() => Assert.Equal(0, DataMerge.Run(Here, [Here, Path.Combine(_root, "missing")]));
}
