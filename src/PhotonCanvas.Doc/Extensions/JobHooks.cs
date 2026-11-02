namespace PhotonCanvas.Doc.Extensions;

/// <summary>
/// A step an add-on adds to turning a design into a job. Hooks run in the order they were added, on
/// whatever thread builds the job (often a background one), so they must not touch the user interface.
/// A hook that throws is skipped for that build and its message is shown as a warning.
/// </summary>
public interface IJobHook
{
    /// <summary>Shown in warnings ("Grain-aware engraving: …").</summary>
    string Name { get; }

    /// <summary>
    /// Change the design just before it is built, without touching the document the person sees: e.g.
    /// split a fill into regions of different power, or swap a picture for one with its tones adjusted.
    /// Return <paramref name="state"/> itself to leave it alone.
    /// </summary>
    DocState BeforeBuild(DocState state, JobOptions options) => state;

    /// <summary>Extra warnings for the job's checks (out-of-range values your add-on knows about).</summary>
    IEnumerable<string> Check(DocState state, JobOptions options) => [];
}

/// <summary>The job hooks that are on. Add-ons add theirs through the add-on host (which removes them again).</summary>
public static class JobHooks
{
    static readonly object Gate = new();
    static IJobHook[] _hooks = [];

    public static IReadOnlyList<IJobHook> All => _hooks;

    /// <summary>Add a hook; dispose the result to take it away.</summary>
    public static IDisposable Add(IJobHook hook)
    {
        lock (Gate) _hooks = [.. _hooks, hook];
        return new Remover(hook);
    }

    sealed class Remover(IJobHook hook) : IDisposable
    {
        bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            lock (Gate) _hooks = [.. _hooks.Where(h => !ReferenceEquals(h, hook))];
        }
    }

    /// <summary>Run every hook's <see cref="IJobHook.BeforeBuild"/>; problems become warnings.</summary>
    public static DocState Apply(DocState state, JobOptions options, List<string> warnings)
    {
        foreach (var h in _hooks)
            try { state = h.BeforeBuild(state, options) ?? state; }
            catch (Exception e) { warnings.Add($"{h.Name}: skipped ({e.Message})"); }
        return state;
    }

    /// <summary>Every hook's extra warnings.</summary>
    public static IEnumerable<string> Checks(DocState state, JobOptions options)
    {
        var all = new List<string>();
        foreach (var h in _hooks)
            try { all.AddRange(h.Check(state, options).Select(w => $"{h.Name}: {w}")); }
            catch (Exception e) { all.Add($"{h.Name}: check failed ({e.Message})"); }
        return all;
    }
}
