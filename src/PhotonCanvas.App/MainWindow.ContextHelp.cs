using Avalonia.Controls;

namespace PhotonCanvas.App;

/// <summary>What the main window's parts say about themselves for the right-click help (<see cref="ContextHelp"/>).</summary>
public partial class MainWindow
{
    /// <summary>The panels and bars: their page when nothing in them is explained by itself. The canvas: the shapes picked.</summary>
    void DescribeForHelp()
    {
        static HelpSubject Area(string page, params string[] targets) => new() { Page = page, Targets = targets };
        // Right-clicking the canvas picks what's under the mouse first, so the selection is what was clicked.
        ContextHelp.Describe(Canvas, () => ContextHelp.Shapes(_doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).ToList()));
        ContextHelp.Describe(_layers, () => Area("layers.md", "panel:Layers"));
        ContextHelp.Describe(_props, () => Area("workspace.md#the-selection-panel", "panel:Selection"));
        ContextHelp.Describe(_layerEditor, () => Area("layer-settings.md", "panel:Layer settings"));
        ContextHelp.Describe(_job, () => Area("running-jobs.md", "panel:Job"));
        ContextHelp.Describe(_hud, () => Area("workspace.md#the-machine-panel", "panel:Machine"));
        ContextHelp.Describe(PagesHost, () => Area("workspace.md#the-pages-bar"));
        ContextHelp.Describe(ToolOptions, () => Area("workspace.md#the-tool-options-bar"));
        ContextHelp.Describe(_ribbon, () => Area("workspace.md#the-ribbon"));
        if (StatusText.Parent is Control status) ContextHelp.Describe(status, () => Area("workspace.md#the-status-bar"));
    }

    /// <summary>"Tab/Label" when this control is a ribbon button.</summary>
    internal string? RibbonCommandOf(Control c)
    {
        foreach (var (key, button) in _ribbon.Commands)
            if (button == c) return key;
        return null;
    }
}
