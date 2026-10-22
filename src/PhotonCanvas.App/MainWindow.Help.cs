using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Help;

namespace PhotonCanvas.App;

/// <summary>The help guide and the tutorials, and what the tutorials can point at in this window.</summary>
public partial class MainWindow : ITutorialHost
{
    readonly Dictionary<string, Control> _headerButtons = new(StringComparer.OrdinalIgnoreCase);
    static bool _f1Hooked;

    /// <summary>F1 in any other window opens the guide at the page explaining it.</summary>
    void HookHelpKey()
    {
        if (_f1Hooked) return;
        _f1Hooked = true;
        InputElement.KeyDownEvent.AddClassHandler<Window>((w, e) =>
        {
            if (e.Key != Key.F1 || e.Handled || w is MainWindow or HelpWindow) return;
            e.Handled = true;
            var topic = HelpFiles.Library.ForWindow(w.GetType().Name);
            ShowHelp(topic?.File, topic?.Anchor);
        });
    }

    /// <summary>Open the help guide (at a page and section).</summary>
    public void ShowHelp(string? file = null, string? anchor = null) => HelpWindow.Show(this, StartTutorial, ShowTutorials, file, anchor);

    /// <summary>The tutorials, as a list to pick from.</summary>
    public void ShowTutorials() => TutorialsWindow.Show(this, StartTutorial, ReadGuide);

    /// <summary>Start a tutorial by its id.</summary>
    public void StartTutorial(string id)
    {
        if (HelpFiles.Library.Tutorials.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) is not { } tutorial)
        {
            StatusText.Text = $"There's no tutorial called {id}.";
            return;
        }
        TutorialsWindow.CloseIfOpen();
        Activate();
        TutorialRunner.Start(this, tutorial, ReadGuide);
    }

    /// <summary>A link to the guide (page.md#section).</summary>
    void ReadGuide(string target)
    {
        int hash = target.IndexOf('#');
        ShowHelp(hash < 0 ? target : target[..hash], hash < 0 ? null : target[(hash + 1)..]);
    }

    Window ITutorialHost.Main => this;

    Control? ITutorialHost.Landmark(string kind, string value)
    {
        switch (kind)
        {
            case "ribbon":
                int slash = value.IndexOf('/');
                return slash < 0 ? null : _ribbon.Show(value[..slash].Trim(), value[(slash + 1)..].Trim());
            case "tab":
                return _ribbon.TabHeader(value);
            case "header":
                return _headerButtons.GetValueOrDefault(value);
            case "panel":
                return value.ToLowerInvariant() switch
                {
                    "job" => JobHost,
                    "machine" => _hud,
                    _ => _docking.Show(value),
                };
            case "canvas":
                return Canvas;
            default:
                return null;
        }
    }

    TutorialSnapshot ITutorialHost.Counts() => new(_doc.State.Shapes.Count, _doc.State.Layers.Count, Canvas.Zoom, _doc.Selection.Count, SelectionKey());

    /// <summary>Which shapes are selected, as one number (changes when the selection does).</summary>
    int SelectionKey() => _doc.Selection.Aggregate(17, (h, id) => h ^ id.GetHashCode());

    bool? ITutorialHost.Met(string kind, string value, TutorialSnapshot atStart)
    {
        static int N(string v, int fallback) => int.TryParse(v.TrimStart('+'), out var n) ? n : fallback;
        return kind switch
        {
            // A selection made during the step: not one that was already there when it began.
            "selection" => _doc.Selection.Count >= N(value, 1) && (atStart.Selected < N(value, 1) || SelectionKey() != atStart.SelectionKey),
            "shapes" => _doc.State.Shapes.Count >= atStart.Shapes + N(value, 1),
            "layers" => _doc.State.Layers.Count >= atStart.Layers + N(value, 1),
            "tool" => string.Equals(Canvas.Tool.ToString(), value, StringComparison.OrdinalIgnoreCase),
            "tab" => string.Equals(_settings.RibbonTab, value, StringComparison.OrdinalIgnoreCase),
            "layer-mode" => string.Equals(_doc.State.FindLayer(_doc.ActiveLayer)?.Mode.ToString(), value, StringComparison.OrdinalIgnoreCase),
            "zoom" => Math.Abs(Canvas.Zoom - atStart.Zoom) > 1e-3,
            "picture-selected" => SelectedPicture() is not null,
            _ => null,
        };
    }
}
