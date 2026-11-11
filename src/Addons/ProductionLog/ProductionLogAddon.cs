using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Extensions;

namespace PhotonCanvas.Addon.ProductionLog;

/// <summary>
/// Machine → Job → Production log: every job sent from here is written down (when, how it ended, the
/// serial numbers, spreadsheet rows and text it marked, its settings), so serial marking can be traced
/// and carried on after a stop or a crash, and the job's checks warn before a serial is marked twice.
/// </summary>
public sealed class ProductionLogAddon : IAddon
{
    IAddonHost _host = null!;
    LogStore _store = null!;
    LogBook _book = null!;
    volatile string _design = "Untitled";
    ProductionLogWindow? _window;

    internal const string Icon = "F0 M5 2h10l4 4v16H5z M7 4v16h10V7h-3V4z M9 10h6v1.5H9z M9 13.5h6V15H9z M9 17h4v1.5H9z";

    public void Start(IAddonHost host)
    {
        _host = host;
        _store = new LogStore(host.DataFolder);
        _book = _store.Book;
        _design = LogRuns.DesignName(host.Document.FilePath);

        host.JobSent += Sent;
        host.JobStarted += Started;
        host.JobEnded += Ended;
        host.DocumentChanged += () => _design = LogRuns.DesignName(_host.Document.FilePath);
        host.AddJobHook(new MarkedBefore(this));
        host.AddRibbonButton(new RibbonButton("Machine", "Job", "Production log", Icon, Open)
        {
            Tip = "Every job sent to the machine: when, how it ended, the serial numbers and rows it marked, and its settings. Carry on serial marking where it stopped.",
            Order = 21,
        });
        host.AddCommand(new AddonCommand("production-log.open", "Job", "Production log…", "", Open));
    }

    void Sent(UploadedJob j)
    {
        var run = LogRuns.From(j.State, j.DesignFile, j.SentAt, j.Part, j.CompletesRun, j.Job.EstimateS, j.Job.Bounds, j.Machine, Environment.UserName);
        _book.Add(run);
        _book.LiveId = run.Id;
    }

    void Started(string name)
    {
        // Only the job this program sent: a job started from the machine itself is called "Job".
        if (_book.LiveId is not { } id || _book.Find(id) is not { StartedAt: null } run) return;
        if (!string.Equals(name, run.Design, StringComparison.OrdinalIgnoreCase)) return;
        _book.Started(id, DateTimeOffset.Now);
    }

    void Ended(bool finished)
    {
        if (_book.LiveId is not { } id || _book.Find(id) is not { StartedAt: not null, EndedAt: null } run) return;
        _book.Ended(id, DateTimeOffset.Now, finished);
        _book.LiveId = null;
        if (finished && run.Items.Any(i => i.Serial is not null || i.Row is not null))
            _host.Status($"Production log: {run.ItemsText()} of {run.Design} marked{(run.CompletesRun ? "" : $" ({run.Part}, more to come)")}.");
    }

    void Open()
    {
        if (_window is { } open) { open.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            _window = new ProductionLogWindow(_host, _store);
            try { await _window.ShowDialog(_host.Owner); }
            finally { _window = null; }
        });
    }

    public void Stop() => _window?.Close();

    /// <summary>The job's checks: a warning when the log says a serial number or row was already marked.</summary>
    sealed class MarkedBefore(ProductionLogAddon addon) : IJobHook
    {
        public string Name => "Production log";

        public IEnumerable<string> Check(DocState state, JobOptions options)
        {
            if (!addon._store.Settings.WarnAgain) return [];
            var items = LogRuns.Items(state);
            return items.Count == 0 ? [] : LogRuns.AlreadyMarked(addon._book.Runs, addon._design, items);
        }
    }
}
