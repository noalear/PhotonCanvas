using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>
/// A line drawn all the way round the object on the rotary (top edge of the unrolled area to the
/// bottom, ending where it started once round) is a closed loop on the object, but an open line on
/// the page. Cut once, it starts and stops at the same spot: the rotary is still getting up to speed
/// there and slowing down at the end, so the beam (whose power follows the speed) leaves a short
/// stretch at the seam not cut through (Kyle, 2026-11-09). So such a line carries on round past
/// where it started, the same as a closed outline's overcut.
/// </summary>
public static class RotarySeam
{
    /// <summary>How far past the start a line all the way round carries on (mm round the object), unless the layer's overcut is longer.</summary>
    public const double OverlapMm = 2;

    /// <summary>The longest the carry-on can be (mm): the job's area check allows this much past once round.</summary>
    public const double MaxOverlapMm = Job.RotarySeamSlackMm;

    /// <summary>
    /// The open path goes once round an object <paramref name="circumferenceMm"/> round: its ends are at the
    /// same place across, a circumference apart (up to a millimetre short, which the carry-on closes too).
    /// </summary>
    public static bool GoesRound(IReadOnlyList<Vec2> points, double circumferenceMm)
    {
        if (points.Count < 2 || circumferenceMm <= 2) return false;
        var a = points[0];
        var b = points[^1];
        double dy = Math.Abs(b.Y - a.Y);
        return Math.Abs(b.X - a.X) <= 0.5 && dy <= circumferenceMm + 1e-6 && dy >= circumferenceMm - 1;
    }

    /// <summary>The path, then on round past its start by <paramref name="overlapMm"/> (following its own first stretch, once round on).</summary>
    public static List<Vec2> CarryOn(IReadOnlyList<Vec2> points, double circumferenceMm, double overlapMm)
    {
        var o = new List<Vec2>(points);
        if (overlapMm <= 0 || points.Count < 2) return o;
        var shift = new Vec2(0, Math.Sign(points[^1].Y - points[0].Y) * circumferenceMm);
        var prev = points[0];
        // (Already there when it ends exactly once round.)
        if ((prev + shift - o[^1]).Length > 1e-9) o.Add(prev + shift);
        double left = overlapMm;
        for (int i = 1; i < points.Count && left > 1e-9; i++)
        {
            var next = points[i];
            double len = (next - prev).Length;
            if (len >= left)
            {
                o.Add(prev + (next - prev) * (left / len) + shift);
                break;
            }
            o.Add(next + shift);
            left -= len;
            prev = next;
        }
        return o;
    }
}
