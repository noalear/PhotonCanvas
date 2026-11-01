using Avalonia.Controls;

namespace PhotonCanvas.App.Addons;

/// <summary>
/// Named places in windows where add-ons can put a panel of their own. A window calls
/// <see cref="Fill"/> with a container when it opens; panels added later (an add-on turned on while
/// the window is open) appear the next time it opens.
/// Slot names are "window.place"; the ones there are, and what each passes as
/// <see cref="SlotContext.Data"/>, are listed in docs/addons.md. Add-ons can make slots in their own
/// windows the same way.
/// </summary>
public static class AddonSlots
{
    static readonly List<(string Slot, Func<SlotContext, Control?> Make, object Owner)> Panels = [];

    internal static IDisposable Add(string slot, Func<SlotContext, Control?> make, object owner)
    {
        var entry = (slot, make, owner);
        Panels.Add(entry);
        return new AddonUndo(() => Panels.Remove(entry));
    }

    /// <summary>Is any add-on's panel waiting for this slot.</summary>
    public static bool Any(string slot) => Panels.Any(p => p.Slot == slot);

    /// <summary>Put every panel made for <paramref name="slot"/> into <paramref name="into"/>. Returns how many.</summary>
    public static int Fill(string slot, Panel into, Window window, object? data = null, Action? changed = null)
    {
        var ctx = new SlotContext { Slot = slot, Window = window, Data = data, Changed = changed ?? (() => { }) };
        int n = 0;
        foreach (var (s, make, _) in Panels.ToList())
        {
            if (s != slot) continue;
            Control? c;
            try { c = make(ctx); }
            catch (Exception e) { AppLog.Write("addons", $"Add-on panel for {slot} failed: {e.Message}"); continue; }
            if (c is null) continue;
            into.Children.Add(c);
            n++;
        }
        return n;
    }
}

sealed class AddonUndo(Action undo) : IDisposable
{
    Action? _undo = undo;
    public void Dispose()
    {
        _undo?.Invoke();
        _undo = null;
    }
}
