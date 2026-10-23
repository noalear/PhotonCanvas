namespace PhotonCanvas.Remote.App;

/// <summary>What the device the app runs on can do, beyond drawing screens.</summary>
public interface IPhone
{
    /// <summary>The app's own folder (the saved connection lives there).</summary>
    string DataFolder { get; }
    /// <summary>The app's version, as the store shows it.</summary>
    string Version { get; }
    /// <summary>Can it scan a QR code with the camera?</summary>
    bool CanScan { get; }
    /// <summary>Scan a QR code; what it holds, or null if cancelled or unavailable.</summary>
    Task<string?> ScanQrAsync();
    /// <summary>A short buzz (a job ended), or a long one (an alarm).</summary>
    void Vibrate(bool alarm);
    /// <summary>Open a web page in the browser.</summary>
    void OpenWeb(string url);
}

/// <summary>A device with none of it (headless runs, tests).</summary>
public sealed class NoPhone : IPhone
{
    public string DataFolder { get; init; } = Path.Combine(Path.GetTempPath(), "PhotonCanvasRemote");
    public string Version => "dev";
    public bool CanScan => false;
    public Task<string?> ScanQrAsync() => Task.FromResult<string?>(null);
    public void Vibrate(bool alarm) { }
    public void OpenWeb(string url) { }
}
