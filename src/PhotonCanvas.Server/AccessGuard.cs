using System.Collections.Concurrent;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Server;

/// <summary>
/// Every /api request must carry the access code: as <c>Authorization: Bearer &lt;code&gt;</c>, or as
/// <c>?access_token=&lt;code&gt;</c> where a header can't be set (pictures in a web page, the event stream).
/// Ten wrong codes from one address lock it out for five minutes.
/// </summary>
public sealed class AccessGuard(RemoteConfig config)
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(5);

    readonly ConcurrentDictionary<string, (int Failures, DateTime Since)> _failures = new();

    /// <summary>Null when the request may go ahead; otherwise the HTTP status and why not.</summary>
    public (int Status, string Error)? Check(string? remoteAddress, string? authorizationHeader, string? accessToken, DateTime now)
    {
        string who = remoteAddress ?? "?";
        if (_failures.TryGetValue(who, out var f) && f.Failures >= MaxFailures && now - f.Since < Lockout)
            return (429, "Too many wrong access codes from this device. Wait 5 minutes, then try again.");
        string? given = authorizationHeader is { } h && h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h[7..] : accessToken;
        if (config.CodeMatches(given))
        {
            _failures.TryRemove(who, out _);
            return null;
        }
        if (!string.IsNullOrWhiteSpace(given))
            _failures.AddOrUpdate(who, (1, now), (_, old) => now - old.Since >= Lockout ? (1, now) : (old.Failures + 1, old.Since));
        return (401, "Wrong or missing access code. It's in the desktop app: Machine → Remote access.");
    }
}
