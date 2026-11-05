using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotonCanvas.Remote;

// What the remote server sends and receives (docs/remote-api.md). The server builds these and the
// phone reads them, so both always agree. JSON is camelCase.

/// <summary>GET /api/info.</summary>
/// <param name="Api">The API's version: the phone can tell an older server.</param>
/// <param name="AndroidApp">Where the server offers the Android app for download, if it has it.</param>
/// <param name="Addresses">The computer's addresses (home network, Tailscale): the phone keeps them and uses whichever answers, at home or away.</param>
public sealed record ServerInfo(string Product, string? Version, string? Machine, string CameraNote, int Api = ServerInfo.CurrentApi, string? AndroidApp = null, string[]? Addresses = null)
{
    public const int CurrentApi = 1;
}

/// <summary>The job on the machine.</summary>
/// <param name="Source">"phone", "computer" (the desktop app) or "elsewhere".</param>
/// <param name="Stage">"loaded", "running" or "paused".</param>
public sealed record JobStatus(
    string TaskId, string Design, string Source, string Stage, DateTimeOffset? LoadedAt, DateTimeOffset? StartedAt,
    double ElapsedS, double? Fraction, double? RemainingS, double? EstimateS, bool StartedFromPhone);

/// <summary>GET /api/status, and every "status" event.</summary>
/// <param name="Mode">The machine's own word: P_IDLE, P_WORK (loaded), P_WORKING, P_PAUSE, P_WORK_DONE…</param>
/// <param name="State">The mode in plain words.</param>
/// <param name="Problem">Why the server isn't connected to the machine, if it isn't.</param>
/// <param name="CameraBlanked">The machine blanks its cameras while a job runs: pictures come out black.</param>
public sealed record MachineStatus(
    bool Connected, string? Ip, string? Name, string? Firmware, string Mode, string State,
    double? FocusDistanceMm, string? Problem, JobStatus? Job, bool CameraBlanked, DateTimeOffset At);

/// <summary>One design the phone can load (GET /api/projects).</summary>
public sealed record ProjectItem(string Id, string Name, string Folder, DateTimeOffset Modified, bool Recent);

/// <summary>A picture the server took and keeps for a while.</summary>
public sealed record PictureInfo(string Id, DateTimeOffset TakenAt, bool Blanked, string Url);

/// <summary>POST /api/projects/{id}/check: what Ship It would show before sending.</summary>
/// <param name="Refusal">Set when it can't be loaded, and why.</param>
/// <param name="LoadedJob">A job is loaded and not started: loading this one replaces it.</param>
/// <param name="PhoneNote">Something the phone does differently from the desktop app for this design.</param>
/// <param name="Text">The whole message, as Ship It shows it.</param>
/// <param name="Preview">URL of a picture of what will be marked.</param>
public sealed record CheckResult(
    string Id, string Design, double EstimateS, string Estimate, double WidthMm, double HeightMm,
    string? Refusal, IReadOnlyList<string> NotInJob, IReadOnlyList<string> Lines, IReadOnlyList<string> Warnings,
    string? LoadedJob, string? PhoneNote, string Text, string Preview);

/// <summary>POST /api/projects/{id}/load: the job is on the machine, waiting; a picture of the bed to start on.</summary>
public sealed record LoadResult(string TaskId, string Design, double EstimateS, PictureInfo? Picture, string Text);

/// <summary>The body of POST /api/job/start: the picture the user looked at.</summary>
public sealed record StartRequest(string? PictureId);

/// <summary>What the machine is doing after start, pause, resume or stop.</summary>
public sealed record ModeResult(string Mode, string State);

/// <summary>A job the server saw run (GET /api/history), with picture URLs (or null).</summary>
/// <param name="ProjectId">The design it came from, when the phone loaded it and the file is still there ("Run again").</param>
public sealed record HistoryItem(
    string Id, string Design, string Source, string SourceText, DateTimeOffset? LoadedAt, DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    bool Finished, bool StartedFromPhone, double? EstimateS, double? TookS, string? Before, string? After, string? ProjectId = null);

/// <summary>GET /tls (no access code): where the encrypted connection is, and its certificate's fingerprint.</summary>
public sealed record TlsInfo(int Port, string Fingerprint);

/// <summary>A "job" event.</summary>
/// <param name="Kind">"started", "finished", "stopped", or "picture" (the after picture is saved).</param>
public sealed record JobEvent(string Kind, string? Id, string? Design, string? Source = null, bool StartedFromPhone = false, double? TookS = null, string? Text = null);

/// <summary>An "alert" event: the machine's alarms.</summary>
/// <param name="Kind">"flame", "tilt" or "machine".</param>
public sealed record AlertEvent(string Kind, string Text, DateTimeOffset At);

/// <summary>Any error: plain words for the user.</summary>
public sealed record ErrorBody(string Error);

/// <summary>Where things are on the server.</summary>
public static class Routes
{
    public const string Info = "/api/info";
    public const string Status = "/api/status";
    public const string Events = "/api/events";
    public const string Projects = "/api/projects";
    public const string History = "/api/history";
    public const string Start = "/api/job/start";
    public const string Tls = "/tls";
    public static string Camera(string camera) => $"/api/camera/{camera}";
    public static string Picture(string id) => $"/api/pictures/{id}";
    public static string Check(string id) => $"/api/projects/{id}/check";
    public static string Preview(string id) => $"/api/projects/{id}/preview.png";
    public static string Load(string id) => $"/api/projects/{id}/load";
    public static string Control(string action) => $"/api/job/{action}";
}

/// <summary>The JSON for all of it, worked out at build time (the Android app is trimmed).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(MachineStatus))]
[JsonSerializable(typeof(List<ProjectItem>))]
[JsonSerializable(typeof(CheckResult))]
[JsonSerializable(typeof(LoadResult))]
[JsonSerializable(typeof(StartRequest))]
[JsonSerializable(typeof(ModeResult))]
[JsonSerializable(typeof(List<HistoryItem>))]
[JsonSerializable(typeof(JobEvent))]
[JsonSerializable(typeof(AlertEvent))]
[JsonSerializable(typeof(ErrorBody))]
[JsonSerializable(typeof(ConnectionInfo))]
[JsonSerializable(typeof(TlsInfo))]
[JsonSerializable(typeof(PhoneSettings))]
public sealed partial class RemoteJson : JsonSerializerContext;
