using System.Collections.Immutable;

namespace PhotonCanvas.Doc;

/// <summary>
/// The open document: current state, selection, and undo/redo history.
/// Not thread-safe; use from the UI thread.
/// </summary>
public sealed class Document
{
    readonly record struct Snapshot(DocState State, ImmutableHashSet<Guid> Selection, Guid ActiveLayer, string Label);

    const int MaxHistory = 500;

    readonly LinkedList<Snapshot> _undo = new();
    readonly Stack<Snapshot> _redo = new();
    Snapshot? _editBase;

    public DocState State { get; private set; }
    public ImmutableHashSet<Guid> Selection { get; private set; } = [];
    /// <summary>Layer that new and imported shapes go to.</summary>
    public Guid ActiveLayer { get; private set; }
    public string? FilePath { get; set; }
    /// <summary>True when there are changes since the last save.</summary>
    public bool Dirty { get; private set; }

    /// <summary>State or selection changed. Argument: true if the shapes/layers changed.</summary>
    public event Action<bool>? Changed;

    public Document() : this(DocState.New()) { }

    public Document(DocState state)
    {
        State = state;
        ActiveLayer = state.Layers.Count > 0 ? state.Layers[0].Id : Guid.Empty;
    }

    public bool CanUndo => _undo.Count > 0 && _editBase is null;
    public bool CanRedo => _redo.Count > 0 && _editBase is null;
    public string? UndoLabel => _undo.Last?.Value.Label;

    /// <summary>What each undo step did, oldest first (the last is the most recent).</summary>
    public IReadOnlyList<string> UndoLabels => _undo.Select(s => s.Label).ToList();

    /// <summary>What each redo step would do again, next first.</summary>
    public IReadOnlyList<string> RedoLabels => _redo.Select(s => s.Label).ToList();

    /// <summary>Undo (negative) or redo (positive) this many steps.</summary>
    public void Step(int steps)
    {
        for (; steps < 0 && CanUndo; steps++) Undo();
        for (; steps > 0 && CanRedo; steps--) Redo();
    }
    public string? RedoLabel => _redo.Count > 0 ? _redo.Peek().Label : null;

    Snapshot Capture(string label) => new(State, Selection, ActiveLayer, label);

    void Restore(Snapshot s)
    {
        State = s.State;
        Selection = s.Selection;
        ActiveLayer = s.ActiveLayer;
        FixUp();
    }

    /// <summary>Keep the selection and active layer pointing at things that exist.</summary>
    void FixUp()
    {
        var ids = State.Shapes.Select(s => s.Id).ToHashSet();
        if (Selection.Any(id => !ids.Contains(id))) Selection = Selection.Where(ids.Contains).ToImmutableHashSet();
        if (State.FindLayer(ActiveLayer) is null)
            ActiveLayer = State.Layers.Count > 0 ? State.Layers[0].Id : Guid.Empty;
    }

    string? _coalesceKey;
    DateTime _coalesceAt;

    /// <summary>
    /// Apply an edit as one undo step. No-op edits are not recorded. Edits with
    /// the same <paramref name="coalesceKey"/> in quick succession (spinner
    /// clicks, typing into one field) merge into a single step.
    /// </summary>
    public void Commit(string label, DocState next, IEnumerable<Guid>? select = null, string? coalesceKey = null)
    {
        if (_editBase is not null) throw new InvalidOperationException("an interactive edit is in progress");
        if (ReferenceEquals(next, State) && select is null) return;
        // Add-ons' rules (copies that follow what they copy), in the same undo step.
        next = Extensions.DocRules.Apply(State, next);
        // Text and codes with fields show their current values.
        if (!ReferenceEquals(next, State)) next = VariableText.Refresh(next, DateTime.Now);
        if (!ReferenceEquals(next, State))
        {
            var now = DateTime.UtcNow;
            bool merge = coalesceKey is not null && coalesceKey == _coalesceKey && _undo.Count > 0 &&
                         _redo.Count == 0 && now - _coalesceAt < TimeSpan.FromSeconds(2);
            if (!merge)
            {
                _undo.AddLast(Capture(label));
                if (_undo.Count > MaxHistory) _undo.RemoveFirst();
            }
            _coalesceKey = coalesceKey;
            _coalesceAt = now;
            _redo.Clear();
            Dirty = true;
        }
        State = next;
        if (select is not null) Selection = select.ToImmutableHashSet();
        FixUp();
        Changed?.Invoke(true);
    }

    /// <summary>Start a live edit (dragging). Updates are shown but not recorded until <see cref="EndEdit"/>.</summary>
    public void BeginEdit()
    {
        _coalesceKey = null;
        if (_editBase is null) _editBase = Capture("");
    }

    public void UpdateEdit(DocState next)
    {
        if (_editBase is null) throw new InvalidOperationException("no edit in progress");
        State = Extensions.DocRules.Apply(State, next);
        Changed?.Invoke(true);
    }

    public void EndEdit(string label)
    {
        if (_editBase is not { } b) return;
        _editBase = null;
        if (!ReferenceEquals(b.State, State))
        {
            _undo.AddLast(b with { Label = label });
            if (_undo.Count > MaxHistory) _undo.RemoveFirst();
            _redo.Clear();
            Dirty = true;
        }
        FixUp();
        Changed?.Invoke(true);
    }

    public void CancelEdit()
    {
        if (_editBase is not { } b) return;
        _editBase = null;
        Restore(b);
        Changed?.Invoke(true);
    }

    public bool InEdit => _editBase is not null;

    public void Undo()
    {
        if (!CanUndo) return;
        _coalesceKey = null;
        var s = _undo.Last!.Value;
        _undo.RemoveLast();
        _redo.Push(Capture(s.Label));
        Restore(s);
        Dirty = true;
        Changed?.Invoke(true);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var s = _redo.Pop();
        _undo.AddLast(Capture(s.Label));
        Restore(s);
        Dirty = true;
        Changed?.Invoke(true);
    }

    public void Select(IEnumerable<Guid> ids)
    {
        var next = ids.ToImmutableHashSet();
        if (next.SetEquals(Selection)) return;
        Selection = next;
        FixUp();
        Changed?.Invoke(false);
    }

    public void SetActiveLayer(Guid id)
    {
        if (id == ActiveLayer || State.FindLayer(id) is null) return;
        ActiveLayer = id;
        Changed?.Invoke(false);
    }

    /// <summary>Mark as having unsaved changes (a design restored after a crash).</summary>
    public void MarkDirty()
    {
        Dirty = true;
        Changed?.Invoke(false);
    }

    public void MarkSaved(string path)
    {
        FilePath = path;
        Dirty = false;
        Changed?.Invoke(false);
    }

    /// <summary>Replace everything (open file / new). Clears history.</summary>
    public void Reset(DocState state, string? path)
    {
        _undo.Clear();
        _redo.Clear();
        _editBase = null;
        State = VariableText.Refresh(state, DateTime.Now);
        Selection = [];
        ActiveLayer = state.Layers.Count > 0 ? state.Layers[0].Id : Guid.Empty;
        FilePath = path;
        Dirty = false;
        Changed?.Invoke(true);
    }
}
