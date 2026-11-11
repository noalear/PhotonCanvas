namespace PhotonCanvas.Doc.Extensions;

/// <summary>
/// A rule an add-on keeps the design to: given the design before an edit and after it, it returns the
/// design as it should be (e.g. copies that follow the shape they copy). It runs on every edit, as
/// part of that edit's undo step, and while dragging, so it must be quick, and return
/// <paramref name="after"/> itself (the same object) when it has nothing to change.
/// </summary>
public interface IDocRule
{
    /// <summary>Named in the log if the rule throws (it is then skipped for that edit).</summary>
    string Name { get; }

    DocState Apply(DocState before, DocState after);
}

/// <summary>The design rules that are on. Add-ons add theirs through the add-on host (which removes them again).</summary>
public static class DocRules
{
    static readonly object Gate = new();
    static IDocRule[] _rules = [];

    public static IReadOnlyList<IDocRule> All => _rules;

    /// <summary>Add a rule; dispose the result to take it away.</summary>
    public static IDisposable Add(IDocRule rule)
    {
        lock (Gate) _rules = [.. _rules, rule];
        return new Remover(rule);
    }

    sealed class Remover(IDocRule rule) : IDisposable
    {
        bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            lock (Gate) _rules = [.. _rules.Where(r => !ReferenceEquals(r, rule))];
        }
    }

    /// <summary>Every rule in turn on an edit from <paramref name="before"/> to <paramref name="after"/>. A rule that throws is skipped.</summary>
    public static DocState Apply(DocState before, DocState after)
    {
        var rules = _rules;
        if (rules.Length == 0 || ReferenceEquals(before, after)) return after;
        foreach (var r in rules)
            try { after = r.Apply(before, after) ?? after; }
            catch (Exception) { } // the host logs it (a broken rule never stops an edit)
        return after;
    }
}
