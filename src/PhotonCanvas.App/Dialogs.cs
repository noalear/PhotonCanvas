using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotonCanvas.App;

/// <summary>Small modal dialogs (message, confirm, text prompt).</summary>
static class Dialogs
{
    static Window Shell(string title, Control body, double width = 420) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false,
        Background = UiKit.Brush("Bg1"),
        Content = new Border { Padding = new Thickness(20), Child = body },
    };

    static StackPanel Buttons(params Button[] buttons)
    {
        var p = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
        };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    static Control Body(string message, IBrush? accent = null) => new TextBlock
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        Foreground = accent ?? UiKit.Brush("TextMain"),
    };

    public static async Task Message(Window owner, string title, string message)
    {
        var ok = new Button { Content = "OK", IsDefault = true, Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80 };
        var w = Shell(title, new StackPanel { Children = { Body(message), Buttons(ok) } });
        ok.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
    }

    /// <summary>Yes/no. Returns true for the affirmative button.</summary>
    public static async Task<bool> Confirm(Window owner, string title, string message, string yes = "OK", string no = "Cancel")
    {
        bool result = false;
        var y = new Button { Content = yes, IsDefault = true, Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        var n = new Button { Content = no, IsCancel = true, MinWidth = 90 };
        var w = Shell(title, new StackPanel { Children = { Body(message), Buttons(n, y) } });
        y.Click += (_, _) => { result = true; w.Close(); };
        n.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
        return result;
    }

    /// <summary>Three-way choice for unsaved changes: true = save, false = discard, null = cancel.</summary>
    public static async Task<bool?> SaveChanges(Window owner, string docName)
    {
        bool? result = null;
        var save = new Button { Content = "Save", IsDefault = true, Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        var discard = new Button { Content = "Don't save", MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var w = Shell("Unsaved changes", new StackPanel { Children = { Body($"Save changes to \"{docName}\"?"), Buttons(cancel, discard, save) } });
        save.Click += (_, _) => { result = true; w.Close(); };
        discard.Click += (_, _) => { result = false; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> Prompt(Window owner, string title, string label, string initial = "")
    {
        string? result = null;
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        var w = Shell(title, new StackPanel { Children = { Body(label), box, Buttons(cancel, ok) } });
        ok.Click += (_, _) => { result = box.Text; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { result = box.Text; w.Close(); } };
        w.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await w.ShowDialog(owner);
        return result;
    }
}
