using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using YTool.Core;
using YTool.Doc;
using static YTool.App.UiKit;

namespace YTool.App;

/// <summary>Grid or circular copies of the selection, previewed live on the canvas.</summary>
public sealed class ArrayWindow : Window
{
    readonly Document _doc;
    readonly DocState _base;
    readonly HashSet<Guid> _ids;
    readonly bool _circular;
    readonly NumericUpDown _cols = Number(1, 500, 1, "0"), _rows = Number(1, 500, 1, "0");
    readonly NumericUpDown _gapX = Number(-1000, 1000, 1, "0.##"), _gapY = Number(-1000, 1000, 1, "0.##");
    readonly NumericUpDown _count = Number(2, 1000, 1, "0");
    readonly NumericUpDown _cx = Number(-1000, 1000, 1, "0.##"), _cy = Number(-1000, 1000, 1, "0.##");
    readonly NumericUpDown _span = Number(-360, 360, 15, "0.#");
    readonly CheckBox _rotate = new() { Content = "Turn each copy to follow the circle", IsChecked = true };
    readonly TextBlock _info = Hint();
    List<Guid> _all = [];
    bool _done;

    public ArrayWindow(Document doc, bool circular, MachineProfile machine)
    {
        _doc = doc;
        _base = doc.State;
        _ids = [.. doc.Selection];
        _circular = circular;
        Title = circular ? "Circular array" : "Grid array";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var box = _base.Bounds(_ids);
        _cols.Value = 3; _rows.Value = 2; _gapX.Value = 5; _gapY.Value = 5;
        _count.Value = 6; _span.Value = 360;
        // Default centre: below the selection, so copies go round it.
        var c = box.IsEmpty ? new Vec2(machine.FieldW / 2, machine.FieldH / 2) : new Vec2(box.Center.X, box.MaxY + Math.Max(box.Width, box.Height));
        _cx.Value = (decimal)Math.Round(c.X, 2);
        _cy.Value = (decimal)Math.Round(c.Y, 2);

        var ok = new Button { Content = "Make copies", IsDefault = true, Classes = { "primary" }, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { _done = true; _doc.EndEdit(Title); _doc.Select(_all); Close(); };
        cancel.Click += (_, _) => Close();
        Closed += (_, _) => { if (!_done) _doc.CancelEdit(); };

        var fields = new StackPanel { Spacing = 10 };
        if (circular)
        {
            fields.Children.Add(Field("Copies", _count, tip: "Total, including the original."));
            fields.Children.Add(Field("Centre X", _cx));
            fields.Children.Add(Field("Centre Y", _cy));
            fields.Children.Add(Field("Spread (°)", _span, tip: "360 = all the way round, evenly spaced. Less = an arc from the original."));
            fields.Children.Add(_rotate);
        }
        else
        {
            fields.Children.Add(Field("Columns", _cols));
            fields.Children.Add(Field("Rows", _rows));
            fields.Children.Add(Field("Gap across (mm)", _gapX, tip: "Space between copies, not their spacing."));
            fields.Children.Add(Field("Gap down (mm)", _gapY));
        }
        fields.Children.Add(_info);
        fields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } });
        Content = new Border { Padding = new Thickness(18), Child = fields };
        foreach (var n in new[] { _cols, _rows, _gapX, _gapY, _count, _cx, _cy, _span }) n.ValueChanged += (_, _) => Preview();
        _rotate.IsCheckedChanged += (_, _) => Preview();
        _doc.BeginEdit();
        Opened += (_, _) => Preview();
    }

    void Preview()
    {
        var (state, all) = _circular
            ? Ops.CircularArray(_base, _ids, (int)(_count.Value ?? 2), new Vec2((double)(_cx.Value ?? 0), (double)(_cy.Value ?? 0)), (double)(_span.Value ?? 360), _rotate.IsChecked == true)
            : Ops.GridArray(_base, _ids, (int)(_cols.Value ?? 1), (int)(_rows.Value ?? 1), (double)(_gapX.Value ?? 0), (double)(_gapY.Value ?? 0));
        _all = all;
        _doc.UpdateEdit(state);
        var b = state.Bounds(all);
        _info.Text = $"{all.Count / Math.Max(1, _ids.Count)} copies, {b.Width:0.#} × {b.Height:0.#} mm overall.";
    }
}
