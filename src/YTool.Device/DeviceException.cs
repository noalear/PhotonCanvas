using System.Text.Json.Nodes;

namespace YTool.Device;

/// <summary>
/// An error reported by the device (positive or device-defined codes), or a local link failure
/// (the negative codes defined here). <see cref="Exception.Message"/> is written for the person
/// using the app.
/// </summary>
public class DeviceException : Exception
{
    /// <summary>The device did not answer in time.</summary>
    public const int TimeoutCode = -1001;
    /// <summary>The client is not connected (never connected, or already disposed).</summary>
    public const int NotConnectedCode = -1002;
    /// <summary>The connection was lost while waiting.</summary>
    public const int DisconnectedCode = -1003;
    /// <summary>The device sent something we could not understand.</summary>
    public const int ProtocolErrorCode = -1004;
    /// <summary>A transferred file failed its MD5 check.</summary>
    public const int ChecksumMismatchCode = -1005;
    /// <summary>The device is not idle, so a job can't be uploaded.</summary>
    public const int BusyCode = -1006;
    /// <summary>Auto-focus ended without a result (stopped, failed or timed out).</summary>
    public const int FocusFailedCode = -1007;

    public DeviceException(int code, string message, JsonNode? data = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        ResponseData = data;
    }

    /// <summary>Device response code, or one of the local *Code constants.</summary>
    public int Code { get; }

    /// <summary>The <c>data</c> member of the failing response, if any.</summary>
    public JsonNode? ResponseData { get; }

    /// <summary>True for session/link problems that a reconnect may fix.</summary>
    public bool IsConnectionProblem => Code is NotConnectedCode or DisconnectedCode or TimeoutCode or 403;

    /// <summary>Builds the exception for a non-zero response <paramref name="code"/> to <paramref name="url"/>.</summary>
    public static DeviceException FromResponse(int code, string? deviceMessage, string url, JsonNode? data = null)
    {
        string what = code switch
        {
            403 => "the session has expired; reconnect to the machine",
            404 => "this firmware does not support the request",
            406 => "the machine is waiting for its button to be pressed to authorize this computer",
            409 => "the machine refused pairing (409: another client may be connected)",
            410 => "the button was not pressed in time",
            503 => "the machine is busy and refused the request (503)",
            _ => $"the machine reported error {code}",
        };
        string detail = string.IsNullOrWhiteSpace(deviceMessage) ? "" : $" (\"{deviceMessage.Trim()}\")";
        return new DeviceException(code, $"{url}: {what}{detail}", data);
    }
}
