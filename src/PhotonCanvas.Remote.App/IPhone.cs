namespace PhotonCanvas.Remote.App;

/// <summary>What the device the app runs on can do, beyond drawing screens.</summary>
public interface IPhone
{
    /// <summary>The app's own folder (the saved connection and settings live there).</summary>
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

    /// <summary>Can it notify, and keep watching the machine with the app closed?</summary>
    bool CanWatch { get; }
    /// <summary>Ask to show notifications (newer Android asks the user once); true when allowed.</summary>
    Task<bool> AllowNotificationsAsync();
    /// <summary>
    /// Keep watching in the background, or stop: the setting, and whether a job is on the machine now.
    /// Called whenever either changes; it starts the watcher when needed (the watcher stops itself when its job is over).
    /// </summary>
    void Watch(WatchMode mode, bool jobOnMachine);
    /// <summary>Show a notification now, to check they arrive.</summary>
    void TestNotification();
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
    public bool CanWatch => false;
    public Task<bool> AllowNotificationsAsync() => Task.FromResult(false);
    public void Watch(WatchMode mode, bool jobOnMachine) { }
    public void TestNotification() { }
}
