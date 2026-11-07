using Avalonia.Media;
using PhotonCanvas.App.View3D;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;

namespace PhotonCanvas.Addon.StockCarve;

/// <summary>
/// The model in the stock, in 3D: the stock a see-through ghost round it, the model solid, red wherever it
/// sticks out of the stock. Laid as it sits in the rotary: the axis left to right, the laser above.
/// </summary>
public sealed class FitView : Model3DView
{
    public FitView() : base(25, 25, -85, 85) { }

    static readonly Color Wood = Color.FromRgb(0xDD, 0xBE, 0x92);
    static readonly Color Out = Color.FromRgb(0xE0, 0x3A, 0x2E);
    static readonly Color Ghost = Color.FromArgb(70, 0x7A, 0xA7, 0xD6);
    static readonly Vec3 Light = new Vec3(-0.3, -0.5, 0.8).Unit;

    /// <summary>Groups: the model, and the stock (which can be hidden).</summary>
    const int ModelGroup = 0, StockGroup = 1;

    public bool ShowStock { get; set { field = value; InvalidateVisual(); } } = true;

    protected override ulong HiddenGroups => ShowStock ? 0 : 1UL << StockGroup;

    protected override Camera3D CameraAt(double yaw, double pitch) => Camera3D.ZUp(yaw, pitch);

    /// <summary>What it shows: the stock (or none), the placed model (or none), and how far each of its triangles sticks out.</summary>
    public void Show(StockMap? stock, IReadOnlyList<Tri>? placed, float[]? stickOut, string? message)
    {
        Scene.Clear();
        Message = message;
        if (placed is not null)
            for (int i = 0; i < placed.Count; i++)
            {
                var t = placed[i];
                Vec3 a = V(t.A), b = V(t.B), c = V(t.C);
                var n = (b - a).Cross(c - a);
                if (n.Length < 1e-12) continue;
                n = n.Unit;
                double lit = 0.6 + 0.4 * Math.Abs(n.Dot(Light));
                bool sticks = stickOut is not null && i < stickOut.Length && stickOut[i] > 0.02f;
                var colour = Scene3D.Shade(sticks ? Out : Wood, lit);
                Scene.AddFace([[a, b, c]], n, colour, ModelGroup);
                // Both sides, so a model with its triangles wound the wrong way still shows.
                Scene.AddFace([[a, c, b]], -n, Scene3D.Shade(colour, 0.8), ModelGroup);
            }
        if (stock is not null) AddStock(stock);
        InvalidateVisual();
    }

    static Vec3 V(Doc.Import.StlDepth.Vec3 p) => new(p.X, p.Y, p.Z);

    /// <summary>The stock's surface, thinned to a size that turns smoothly.</summary>
    void AddStock(StockMap s)
    {
        int nA = s.AnglesDeg.Length, nX = s.Xs.Length;
        int stepA = Math.Max(1, nA / 90), stepX = Math.Max(1, nX / 120);
        var aIdx = Enumerable.Range(0, nA).Where(i => i % stepA == 0).ToList();
        var xIdx = Enumerable.Range(0, nX).Where(i => i % stepX == 0 || i == nX - 1).Distinct().ToList();
        Vec3 P(int a, int k)
        {
            double t = s.AnglesDeg[a] * Math.PI / 180, r = s.R[a, k];
            return new Vec3(s.Xs[k], -r * Math.Sin(t), r * Math.Cos(t));
        }
        int last = s.WholeTurn ? aIdx.Count : aIdx.Count - 1;
        for (int ai = 0; ai < last; ai++)
        {
            int a0 = aIdx[ai], a1 = aIdx[(ai + 1) % aIdx.Count];
            for (int ki = 0; ki + 1 < xIdx.Count; ki++)
            {
                int k0 = xIdx[ki], k1 = xIdx[ki + 1];
                if (double.IsNaN(s.R[a0, k0]) || double.IsNaN(s.R[a0, k1]) || double.IsNaN(s.R[a1, k0]) || double.IsNaN(s.R[a1, k1])) continue;
                Vec3 p00 = P(a0, k0), p01 = P(a0, k1), p10 = P(a1, k0), p11 = P(a1, k1);
                double tm = (s.AnglesDeg[a0] + (a1 > a0 ? s.AnglesDeg[a1] : s.AnglesDeg[a1] + 360)) / 2 * Math.PI / 180;
                var outward = new Vec3(0, -Math.Sin(tm), Math.Cos(tm));
                // Facing out: only the near side of the shell shows, faintly over the model.
                Scene.AddFace([[p00, p01, p11, p10]], outward, Ghost, StockGroup);
            }
        }
    }
}
