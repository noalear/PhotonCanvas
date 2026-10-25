using SkiaSharp;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Server;

/// <summary>A camera picture the server took: what it shows, when, and what the machine was doing.</summary>
/// <param name="Stream">0 = overview, 1 = close-up.</param>
/// <param name="Mode">The machine's mode when it was taken (P_WORKING: the camera is blanked).</param>
/// <param name="TaskId">The job on the machine then, if any.</param>
public sealed record Picture(string Id, int Stream, byte[] Jpeg, DateTime TakenAt, string Mode, string? TaskId)
{
    /// <summary>The machine blanks its cameras for the whole job (seen on firmware 40.54): the picture is black.</summary>
    public bool Blanked => Mode is "P_WORKING";
}

/// <summary>The last few pictures, by id, so the phone can show one and then start the job "on" it.</summary>
public sealed class PictureStore
{
    const int Keep = 16;
    readonly Lock _gate = new();
    readonly LinkedList<Picture> _recent = new();

    public Picture Add(int stream, byte[] jpeg, DateTime at, string mode, string? taskId)
    {
        var p = new Picture(Guid.NewGuid().ToString("N"), stream, jpeg, at, mode, taskId);
        lock (_gate)
        {
            _recent.AddFirst(p);
            while (_recent.Count > Keep) _recent.RemoveLast();
        }
        return p;
    }

    public Picture? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_gate) return _recent.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>The JPEG made smaller to <paramref name="width"/> pixels across (never larger than it is).</summary>
    public static byte[] Scaled(byte[] jpeg, int width, int quality = 80)
    {
        using var src = SKBitmap.Decode(jpeg);
        if (src is null || width <= 0 || width >= src.Width) return jpeg;
        int height = Math.Max(1, (int)Math.Round(src.Height * (double)width / src.Width));
        using var small = src.Resize(new SKImageInfo(width, height), SKFilterQuality.Medium);
        if (small is null) return jpeg;
        using var image = SKImage.FromBitmap(small);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }
}

/// <summary>
/// The safeguard before a job is started from the phone: the start must name a picture the server
/// took in the last two minutes, of the bed with this very job loaded, so someone has looked.
/// </summary>
public static class StartGuard
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Why the phone can't start the job, in plain words; null when it can. Nothing starts from the phone
    /// unless the user has turned that on (<see cref="PhoneStart"/>).
    /// </summary>
    public static string? Problem(bool phoneStartAllowed, Picture? picture, DateTime now, string machineMode, string? loadedTaskId) =>
        phoneStartAllowed ? Problem(picture, now, machineMode, loadedTaskId) : PhoneStart.OffMessage;

    /// <summary>Why the job can't be started on this picture, in plain words; null when it can.</summary>
    public static string? Problem(Picture? picture, DateTime now, string machineMode, string? loadedTaskId)
    {
        if (machineMode != "P_WORK")
            return machineMode is "P_WORKING" or "P_PAUSE" ? "A job is already running on the machine." : "No job is loaded on the machine. Load one first.";
        if (picture is null)
            return "Take a picture of the bed first: a job only starts from the phone after you've looked.";
        if (now - picture.TakenAt > MaxAge)
            return $"That picture is {(int)(now - picture.TakenAt).TotalMinutes} min old. Take a new one, look at it, then start.";
        if (picture.Mode != "P_WORK" || picture.TaskId != loadedTaskId)
            return "That picture was taken before this job was loaded. Take a new one, look at it, then start.";
        return null;
    }
}
