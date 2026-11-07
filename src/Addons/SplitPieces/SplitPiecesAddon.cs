using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.SplitPieces;

/// <summary>
/// Machine → Work area → Split onto pieces (and the right-click item): the selected design split across
/// material outlines, a page per piece; then Placement → Onto pieces → Line up pieces fits each page onto its
/// piece and the ones laid beside it.
/// </summary>
public sealed class SplitPiecesAddon : IAddon
{
    IAddonHost _host = null!;
    LineUpWindow? _lineUp;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Machine", "Work area", "Split onto pieces", "IcBreak", () => _ = host.Guard(Split))
        {
            Tip = "Mark a design across several pieces that go together (four boards making one picture): select the design and the pieces' material outlines; each piece gets a page with its share, and Line up pieces fits it to the pieces on the bed",
            Enabled = () => host.Document.Selection.Count > 0,
            Order = 20,
        });
        host.AddRibbonButton(new RibbonButton("Placement", "Onto pieces", "Line up pieces", "IcLineUp", ShowLineUp)
        {
            Tip = "For a design split onto pieces: the camera keeps looking and moves the page's design onto its piece and the pieces laid beside it, so they line up. No clicking while you place them",
            Large = true,
            Order = 13,
        });
        host.AddContextMenuItem(new ContextMenuEntry("Split onto pieces…", () => _ = host.Guard(Split))
        {
            Visible = () => host.Document.Selection.Count > 0,
            Enabled = () => host.Document.Selection.Count > 1,
        });
        host.AddCommand(new AddonCommand("area.split", "Arrange", "Split onto pieces…", "", () => _ = host.Guard(Split)));
        host.AddCommand(new AddonCommand("camera.lineup", "Camera", "Line up pieces", "", ShowLineUp));
    }

    public void Stop()
    {
        _lineUp?.Close();
        _lineUp = null;
    }

    /// <summary>The selected design split across the selected material outlines (all the ones it touches when none are selected): a page each, then Line up pieces.</summary>
    async Task Split()
    {
        var doc = _host.Document;
        var owner = _host.Owner;
        var s = doc.State;
        var sel = doc.Selection;
        var material = MaterialOutline.Layer(s)?.Id;
        var outlines = MaterialOutline.On(s);
        var pieces = outlines.Where(p => sel.Contains(p.Id)).ToList();
        var art = s.Shapes.Where(x => sel.Contains(x.Id) && x.LayerId != material).Select(x => x.Id).ToHashSet();
        if (pieces.Count == 0 && art.Count > 0)
        {
            var box = s.Bounds(art);
            pieces = [.. outlines.Where(p => p.Bounds.Intersects(box))];
        }
        if (pieces.Count < 2 || art.Count == 0)
        {
            await Dialogs.Message(owner, "Split onto pieces",
                "Select the design and the pieces it goes across. Draw each piece the size it really is (a rectangle 200 × 75 mm for a 200 × 75 mm board), " +
                "right-click it → Use as material outline, and lay them out as they'll go together. Then put the design over them, select it all, and split.\n\n" +
                (pieces.Count < 2 ? $"Pieces found: {pieces.Count} (at least 2 are needed)." : "Nothing of the design is selected."));
            return;
        }
        double w = _host.Machine.FieldW, h = _host.Machine.FieldH;
        _host.Status("Splitting…");
        var result = await Task.Run(() => PieceSplit.Split(s, pieces, art, w, h));
        doc.Commit("Split onto pieces", result.State, []);
        _host.ActivateLayer(doc.ActiveLayer);
        _host.FitWorkArea();
        _host.Status($"Split onto {result.Pieces} pieces: a page each (\"Piece 1 of {result.Pieces}\"…), after the layout. Only each page's piece is sent; the rest shows grey.");
        if (result.Notes.Count > 0) await Dialogs.Message(owner, "Split onto pieces", string.Join("\n", result.Notes));
        ShowLineUp();
    }

    /// <summary>The Line up the pieces window (one at a time).</summary>
    void ShowLineUp()
    {
        if (_lineUp is { IsVisible: true }) { _lineUp.Activate(); return; }
        _lineUp = new LineUpWindow(_host.Document, _host.MachinePanel, _host.Machine);
        _lineUp.Closed += (_, _) => _lineUp = null;
        _lineUp.Show(_host.Owner);
    }
}
