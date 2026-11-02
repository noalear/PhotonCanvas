using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.TimeLapse;

/// <summary>Laser → Job → Time-lapse: a camera photo every few seconds while a job runs, made into a video.</summary>
public sealed class TimeLapseAddon : IAddon
{
    IAddonHost _host = null!;
    TimeLapse? _lapse;

    public void Start(IAddonHost host)
    {
        _host = host;
        var s = host.AppSettings;
        host.AddRibbonButton(new RibbonButton("Laser", "Job", "Time-lapse", "IcCamera", () =>
        {
            s.TimeLapse = !s.TimeLapse;
            host.SaveAppSettings();
            host.RefreshCommands();
        })
        {
            Tip = $"Take a camera photo every {s.TimeLapseSeconds:0} s while a job runs and make a video of it (needs ffmpeg for the video; otherwise the photos are kept). Saved in Pictures.",
            Checked = () => s.TimeLapse,
        });
        host.JobStarted += name =>
        {
            if (s.TimeLapse && _lapse is null) _lapse = TimeLapse.Start(host.MachinePanel.TimeLapseFrameAsync, s.TimeLapseSeconds, name);
        };
        host.JobEnded += ended => _ = Finish();
    }

    /// <summary>A job still running keeps its photos: they're made into the video now.</summary>
    public void Stop() => _ = Finish();

    async Task Finish()
    {
        if (_lapse is not { } lapse) return;
        _lapse = null;
        if (await lapse.FinishAsync() is not { } saved) return;
        _host.Status(File.Exists(saved) ? $"Time-lapse saved: {saved}" : $"Time-lapse photos saved in {saved} (install ffmpeg to get a video).");
    }
}
