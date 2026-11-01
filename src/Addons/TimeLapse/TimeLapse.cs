using PhotonCanvas.Core;

using PhotonCanvas.App;

namespace PhotonCanvas.Addon.TimeLapse;

/// <summary>
/// A time-lapse of a job: a camera photo every few seconds while it runs, made into a video at the
/// end when ffmpeg is installed (we don't ship it), otherwise left as numbered photos. Saved under
/// Pictures/PhotonCanvas time-lapses.
/// </summary>
public sealed class TimeLapse
{
    readonly Func<Task<byte[]>> _snap;
    readonly string _dir;
    readonly CancellationTokenSource _cts = new();
    readonly Task _loop;
    int _frames;

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"{AppIdentity.ProductName} time-lapses");

    TimeLapse(Func<Task<byte[]>> snap, double everySeconds, string name, string? root)
    {
        _snap = snap;
        _dir = Path.Combine(root ?? Root, $"{DateTime.Now:yyyy-MM-dd HHmm} {Safe(name)}");
        Directory.CreateDirectory(_dir);
        _loop = Task.Run(() => Run(TimeSpan.FromSeconds(Math.Clamp(everySeconds, 1, 120))));
    }

    public static TimeLapse Start(Func<Task<byte[]>> snap, double everySeconds, string name, string? root = null) => new(snap, everySeconds, name, root);

    async Task Run(TimeSpan every)
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var jpg = await _snap().ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(_dir, $"frame{++_frames:00000}.jpg"), jpg).ConfigureAwait(false);
            }
            catch (Exception e) when (e is PhotonCanvas.Device.DeviceException or IOException or UnauthorizedAccessException)
            {
                AppLog.Write("time-lapse", e.Message);
            }
            try { await Task.Delay(every, _cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    /// <summary>Stop taking photos and make the video. Returns what was saved (a video, or the folder of photos), or null if nothing was.</summary>
    public async Task<string?> FinishAsync()
    {
        _cts.Cancel();
        await _loop.ConfigureAwait(false);
        if (_frames == 0)
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
        if (FindFfmpeg() is not { } ffmpeg || _frames < 2) return _dir;
        var video = _dir + ".mp4";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            // 10 photos a second; even sizes for the H.264 encoder.
            foreach (var a in new[] { "-y", "-loglevel", "error", "-framerate", "10", "-i", Path.Combine(_dir, "frame%05d.jpg"), "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-pix_fmt", "yuv420p", video })
                psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return _dir;
            var err = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            if (p.ExitCode == 0 && File.Exists(video))
            {
                try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return video;
            }
            AppLog.Write("time-lapse", await err.ConfigureAwait(false));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException)
        {
            AppLog.Write("time-lapse", e.Message);
        }
        return _dir;
    }

    /// <summary>ffmpeg on the PATH (or the usual Mac and Linux places), or null.</summary>
    public static string? FindFfmpeg()
    {
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Concat(["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"]))
        {
            try { if (File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name); }
            catch (ArgumentException) { }
        }
        return null;
    }

    static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string([.. name.Select(c => bad.Contains(c) ? '_' : c)]).Trim();
        return s.Length == 0 ? "job" : s.Length > 40 ? s[..40] : s;
    }
}
