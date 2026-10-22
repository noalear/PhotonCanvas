// The help guide's screenshots, and a walk through every tutorial, in the real program run headless.
// Run from the repository root:
//
//   dotnet run --project tools/HelpShots -- shots [main|windows|extra|more]
//       Retakes the pictures in docs/help/images (all of them, or one group). New pictures no page
//       shows yet are deleted again unless --keep-new is added: show one on its page, or let it go.
//   dotnet run --project tools/HelpShots -- guide
//       The help window and a tutorial running (help-guide-page.png, tutorials-window.png, …).
//   dotnet run --project tools/HelpShots -- walk [tutorial-id,tutorial-id…]
//       Starts every tutorial (or the ones named) and steps through it as a user would, clicking what
//       each step says to click. Prints each step whose target can't be found. Steps that need a
//       connected machine, a camera photo, a 3D model or a burned test are expected in that list.
//
// Nothing here touches the real data folder or a machine: each run uses a fresh scratch folder.

using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.Core;

var repo = FindRepo();
var help = Path.Combine(repo, "docs", "help");
var images = Path.Combine(help, "images");
Environment.SetEnvironmentVariable("PHOTONCANVAS_HELP", help);
Environment.SetEnvironmentVariable(AppIdentity.DataDirVariable, Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-data"));

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

void Pump()
{
    for (int i = 0; i < 5; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}

var what = args.Length > 0 ? args[0] : "";
var rest = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();
switch (what)
{
    case "shots":
        var before = Directory.Exists(images) ? Directory.GetFiles(images, "*.png").Select(Path.GetFileName).ToHashSet() : [];
        DocsSim.Run(images, Pump, rest.FirstOrDefault());
        if (!args.Contains("--keep-new")) DropUnshown(before);
        break;
    case "guide":
        HelpSim.Run(images, Pump, "shots");
        break;
    case "walk":
        if (rest.Count > 0) Environment.SetEnvironmentVariable("HELP_WALK_ONLY", string.Join(",", rest));
        HelpSim.Run(Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-walk"), Pump, "walk");
        break;
    default:
        Console.WriteLine("Usage: dotnet run --project tools/HelpShots -- shots [main|windows|extra|more] [--keep-new] | guide | walk [ids]");
        return 1;
}
return 0;

// New pictures that no page shows: deleted, so the folder only holds what the guide uses.
void DropUnshown(HashSet<string?> before)
{
    var shown = Directory.GetFiles(help, "*.md").SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\]\(images/([^)]+)\)").Select(m => m.Groups[1].Value)).ToHashSet();
    foreach (var f in Directory.GetFiles(images, "*.png"))
    {
        var name = Path.GetFileName(f);
        if (shown.Contains(name)) continue;
        if (before.Contains(name)) Console.WriteLine($"Not on any page (kept, it was there before): {name}");
        else { File.Delete(f); Console.WriteLine($"New, not on any page (deleted; --keep-new keeps it): {name}"); }
    }
}

static string FindRepo()
{
    for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "docs", "help", "contents.md"))) return d.FullName;
    throw new InvalidOperationException("Run this from inside the repository (docs/help wasn't found).");
}
