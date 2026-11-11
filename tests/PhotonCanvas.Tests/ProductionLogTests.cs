using PhotonCanvas.Addon.ProductionLog;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>The production log add-on: what a run marked, the log on disk, marking twice, carrying on.</summary>
public class ProductionLogTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 10, 14, 5, 0, TimeSpan.Zero);

    /// <summary>Three numbered copies of a tag, "No. {serial} {Name}", serial 7 step 2, from row 1 of a spreadsheet.</summary>
    static DocState Tags(long serial = 7, int row = 0)
    {
        var s = DocState.New();
        var text = TextShape.Create("No. {serial} {Name}", new TextStyle("Arial", 5), s.Layers[0].Id, Affine.Identity);
        var copies = Enumerable.Range(0, 3).Select(i => text.With(id: Guid.NewGuid(), transform: Affine.Translate(i * 30, 0)).WithVarOffset(i)).ToList();
        s = Ops.AddShapes(s, copies) with
        {
            Vars = new VariableData { Serial = serial, SerialStep = 2, CsvName = "orders.csv", CsvText = "Name,Order\nAda,100\nBob,101\n\"Cy, Jr\",102\nDee,103\nEd,104\nFay,105\n", CsvRow = row },
        };
        return VariableText.Refresh(s, DateTime.Now);
    }

    static LoggedRun Run(DocState s, string design = "Tags", DateTimeOffset? at = null, bool completes = true) =>
        LogRuns.From(s, $"/designs/{design}.pcanvas", at ?? T0, completes ? null : "section 1 of 2", completes, 60, new Rect2(0, 0, 80, 10), "F2 Ultra", "kyle");

    static string TempLog() => Path.Combine(Path.GetTempPath(), "pc-prodlog-" + Guid.NewGuid().ToString("N"), "production-log.jsonl");

    [Fact]
    public void EachItemKeepsItsSerialRowAndWhatWasMarked()
    {
        var items = LogRuns.Items(Tags());
        Assert.Equal([7L, 9L, 11L], items.Select(i => i.Serial!.Value));
        Assert.Equal([0, 1, 2], items.Select(i => i.Row!.Value));
        Assert.Equal("Text: No. 7 Ada", items[0].Marks.Single());
        Assert.Equal("Text: No. 11 Cy, Jr", items[2].Marks.Single());
        Assert.Equal("Name: Bob, Order: 101", items[1].RowText());
        Assert.Equal("orders.csv", items[0].Sheet);
        var run = Run(Tags());
        Assert.Equal("serials 7–11 · rows 1–3", run.ItemsText());
        Assert.Equal("Tags", run.Design);
        Assert.Single(run.Settings);
        Assert.Contains("mm/s", run.Settings[0]);
    }

    [Fact]
    public void ADesignWithoutFieldsHasNoItems()
    {
        var s = DocState.New();
        s = Ops.AddShapes(s, [TextShape.Create("Hello", new TextStyle("Arial", 5), s.Layers[0].Id, Affine.Identity)]);
        Assert.Empty(LogRuns.Items(s));
        Assert.Equal("no fields", Run(s).ItemsText());
        Assert.Empty(LogRuns.AlreadyMarked([Run(s) with { Finished = true }], "Tags", LogRuns.Items(s)));
    }

    [Fact]
    public void TheLogIsReadBackAsItWasWritten()
    {
        var path = TempLog();
        try
        {
            var book = LogBook.Open(path);
            var a = book.Add(Run(Tags()));
            book.Started(a.Id, T0.AddMinutes(1));
            book.Ended(a.Id, T0.AddMinutes(3), finished: true);
            book.SetSpoiled(a.Id, 1, true);
            book.SetNote(a.Id, "Order 1234, \"rush\"");
            var b = book.Add(Run(Tags(13, 3), at: T0.AddMinutes(10)));
            book.Started(b.Id, T0.AddMinutes(11));
            var c = book.Add(Run(Tags(13, 3), at: T0.AddMinutes(20)));
            book.LiveId = c.Id;
            // A half-written line (a crash while writing) is skipped, and the rest still read.
            File.AppendAllText(path, "{\"t\":\"end\",\"id\":\"");

            var again = LogBook.Open(path);
            Assert.Equal(3, again.Runs.Count);
            var ra = again.Find(a.Id)!;
            Assert.True(ra.Finished);
            Assert.Equal(120, ra.TookS);
            Assert.Equal([1], ra.Spoiled);
            Assert.Equal("Order 1234, \"rush\"", ra.Note);
            Assert.Equal("Text: No. 9 Bob", ra.Items[1].Marks.Single());
            Assert.Equal(RunStatus.Finished, LogRuns.Status(ra, null));
            Assert.Equal(RunStatus.Unknown, LogRuns.Status(again.Find(b.Id)!, null));
            Assert.Equal(RunStatus.Running, LogRuns.Status(again.Find(b.Id)!, b.Id));
            Assert.Equal(RunStatus.NotRun, LogRuns.Status(again.Find(c.Id)!, null));
            Assert.Equal(RunStatus.Waiting, LogRuns.Status(again.Find(c.Id)!, c.Id));

            again.SetSpoiled(a.Id, 1, false);
            Assert.Empty(LogBook.Open(path).Find(a.Id)!.Spoiled);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void MarkingASerialOrRowAgainIsWarnedAbout()
    {
        var done = Run(Tags()) with { StartedAt = T0, EndedAt = T0.AddMinutes(2), Finished = true };
        // The same three again: serials and rows.
        var same = LogRuns.AlreadyMarked([done], "Tags", LogRuns.Items(Tags()));
        Assert.Equal(3, same.Count);
        Assert.Contains("3 of the serial numbers this run marks (7–11) were already marked", same[0]);
        Assert.Contains("rows 1–3", same[1]);
        // One overlapping: serial 11 (and row 3).
        var one = LogRuns.AlreadyMarked([done], "tags", LogRuns.Items(Tags(11, 2)));
        Assert.StartsWith("serial 11 was already marked (on ", one[0]);
        Assert.Contains("spreadsheet row 3 (Name: Cy, Jr, Order: 102)", one[1]);
        // Straight after: nothing.
        Assert.Empty(LogRuns.AlreadyMarked([done], "Tags", LogRuns.Items(Tags(13, 3))));
        // Another design, an untitled one, a stopped run, a first section, or a spoiled item: nothing.
        Assert.Empty(LogRuns.AlreadyMarked([done], "Other", LogRuns.Items(Tags())));
        Assert.Empty(LogRuns.AlreadyMarked([done with { Design = "Untitled" }], "Untitled", LogRuns.Items(Tags())));
        Assert.Empty(LogRuns.AlreadyMarked([done with { Finished = false }], "Tags", LogRuns.Items(Tags())));
        Assert.Empty(LogRuns.AlreadyMarked([done with { CompletesRun = false }], "Tags", LogRuns.Items(Tags())));
        Assert.Empty(LogRuns.AlreadyMarked([done with { Spoiled = [2] }], "Tags", LogRuns.Items(Tags(11, 2))));
    }

    [Fact]
    public void CarryOnStartsWhereTheDesignWouldHaveMovedOn()
    {
        var s = Tags();
        var done = Run(s) with { StartedAt = T0, EndedAt = T0.AddMinutes(2), Finished = true };
        var stopped = Run(VariableText.AfterRun(s, DateTime.Now), at: T0.AddMinutes(5)) with { StartedAt = T0.AddMinutes(5), EndedAt = T0.AddMinutes(6), Finished = false };
        var other = Run(Tags(500, 5), "Other", T0.AddMinutes(9)) with { EndedAt = T0.AddMinutes(10), Finished = true };
        // The design reopened from before the run (never saved after it): Carry on gives what moving on after the run gives.
        var carry = LogRuns.CarryOn([done, stopped, other], "Tags", s.Vars);
        Assert.NotNull(carry);
        var after = VariableText.AfterRun(s, DateTime.Now).Vars;
        Assert.Equal(after.Serial, carry.Value.Vars.Serial);
        Assert.Equal(after.CsvRow, carry.Value.Vars.CsvRow);
        Assert.Equal(13, carry.Value.Vars.Serial);
        Assert.Contains("serials 7–11", carry.Value.From);
        Assert.Null(LogRuns.CarryOn([stopped], "Tags", s.Vars));
        // Mark one again: back to it.
        var back = LogRuns.GoBackTo(done.Items[1], after);
        Assert.Equal(9, back.Serial);
        Assert.Equal(1, back.CsvRow);
        Assert.Equal(2, back.SerialStep);
    }

    [Fact]
    public void TheExportHasOneLinePerItem()
    {
        var done = Run(Tags()) with { StartedAt = T0, EndedAt = T0.AddMinutes(2), Finished = true, Spoiled = [0], Note = "a, b" };
        var csv = Csv.Parse(LogRuns.Csv([done, Run(DocState.New(), "Plain", T0.AddMinutes(9))], null));
        Assert.Equal(4, csv.Rows.Length);
        Assert.Equal("9", csv.Cell(1, csv.Column("Serial")));
        Assert.Equal("Name: Cy, Jr, Order: 102", csv.Cell(2, csv.Column("Row values")));
        Assert.Equal("yes", csv.Cell(0, csv.Column("Spoiled")));
        Assert.Equal("a, b", csv.Cell(0, csv.Column("Note")));
        Assert.Equal("Finished", csv.Cell(0, csv.Column("Status")));
        Assert.Equal("Plain", csv.Cell(3, csv.Column("Design")));
        Assert.Equal("Not run", csv.Cell(3, csv.Column("Status")));
    }

    [Fact]
    public void SearchFindsASerialOrAWord()
    {
        var r = Run(Tags()) with { Note = "rush" };
        Assert.Contains("11", r.SearchText());
        Assert.Contains("Cy, Jr", r.SearchText());
        Assert.Contains("rush", r.SearchText());
    }
}
