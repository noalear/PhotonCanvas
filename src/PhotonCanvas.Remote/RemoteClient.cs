using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Remote;

/// <summary>Something the server (or the network) said no to, in plain words for the user.</summary>
/// <param name="Status">The HTTP status, or 0 when the server couldn't be reached.</param>
public sealed class RemoteError(int status, string message, Exception? inner = null) : Exception(message, inner)
{
    public int Status { get; } = status;
    /// <summary>The access code was wrong (or the phone is locked out for a while), or the computer's certificate changed: scan again.</summary>
    public bool NeedsCode => Status is 401 or 429 or 495;
    public bool Unreachable => Status == 0;
}

/// <summary>A message the server pushes while the phone listens.</summary>
public abstract record RemoteEvent;
public sealed record StatusChanged(MachineStatus Status) : RemoteEvent;
public sealed record JobChanged(JobEvent Job) : RemoteEvent;
public sealed record AlarmRaised(AlertEvent Alert) : RemoteEvent;

/// <summary>A taken picture: the JPEG and what it is.</summary>
public sealed record TakenPicture(byte[] Jpeg, string? Id, DateTimeOffset TakenAt, bool Blanked);

/// <summary>Everything the phone can ask of the machine, through a server (or the demo).</summary>
public interface IRemote : IDisposable
{
    /// <summary>Where it is, for the screen ("192.168.1.50", "Demo machine").</summary>
    string Name { get; }
    bool IsDemo { get; }
    Task<ServerInfo> InfoAsync(CancellationToken ct = default);
    Task<MachineStatus> StatusAsync(CancellationToken ct = default);
    /// <summary>The status now, then every change, until cancelled or the connection drops (then it ends or throws).</summary>
    IAsyncEnumerable<RemoteEvent> EventsAsync(CancellationToken ct = default);
    /// <summary>A picture now: "overview" or "closeup", at most <paramref name="width"/> pixels across.</summary>
    Task<TakenPicture> TakePictureAsync(string camera, int width, CancellationToken ct = default);
    /// <summary>An image the server gave the URL of (a picture, a preview, a history picture).</summary>
    Task<byte[]> ImageAsync(string url, int width, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectItem>> ProjectsAsync(CancellationToken ct = default);
    Task<CheckResult> CheckAsync(string projectId, CancellationToken ct = default);
    Task<LoadResult> LoadAsync(string projectId, CancellationToken ct = default);
    /// <summary>Start the loaded job: the laser fires. Only on a picture taken in the last two minutes with this job loaded.</summary>
    Task<ModeResult> StartAsync(string pictureId, CancellationToken ct = default);
    /// <summary>"pause", "resume" or "stop".</summary>
    Task<ModeResult> ControlAsync(string action, CancellationToken ct = default);
    Task<IReadOnlyList<HistoryItem>> HistoryAsync(int max, CancellationToken ct = default);
}

/// <summary>The real thing: the remote server on the user's computer, over HTTP.</summary>
public sealed class HttpRemote : IRemote
{
    readonly HttpClient _http;
    readonly ConnectionInfo _connection;

    public HttpRemote(ConnectionInfo connection, HttpMessageHandler? handler = null)
    {
        _connection = connection;
        _http = new HttpClient(handler ?? Handler(connection.Fingerprint));
        _http.BaseAddress = connection.BaseUri;
        _http.Timeout = Timeout.InfiniteTimeSpan; // each call sets its own
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Code);
    }

    public string Name => _connection.Display;
    public bool IsDemo => false;
    /// <summary>Encrypted to the computer's own certificate.</summary>
    public bool Secure => _connection.Secure;

    /// <summary>
    /// The connection handler: with a fingerprint, HTTPS accepts that one certificate and nothing else
    /// (it's the computer's own, made by the server, so no certificate authority vouches for it).
    /// </summary>
    static SocketsHttpHandler Handler(string? fingerprint)
    {
        var h = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) };
        if (fingerprint is not null)
            h.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && Fingerprints.Of(cert.GetRawCertData()) == fingerprint;
        return h;
    }

    /// <summary>
    /// The computer at whichever of its addresses answers (home network or Tailscale): all are tried at once,
    /// and the one it was last reached at wins if it answers. Null when none does.
    /// </summary>
    public static async Task<ConnectionInfo?> ReachAsync(ConnectionInfo c, CancellationToken ct = default)
    {
        var addresses = c.AllAddresses.ToList();
        if (addresses.Count == 1) return c;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(6));
        var tries = addresses.Select(async a =>
        {
            using var r = new HttpRemote(c.Using(a));
            try
            {
                await r.InfoAsync(limit.Token).ConfigureAwait(false);
                return a;
            }
            catch (RemoteError) { return null; }
            catch (OperationCanceledException) { return null; }
        }).ToList();
        var answered = new List<string>();
        while (tries.Count > 0)
        {
            var done = await Task.WhenAny(tries).ConfigureAwait(false);
            tries.Remove(done);
            if (await done.ConfigureAwait(false) is not { } a) continue;
            if (a == c.Address) return c;
            answered.Add(a);
            // Give the usual address a moment to answer too before switching.
            if (tries.Count > 0) await Task.WhenAny(Task.WhenAll(tries), Task.Delay(400, ct)).ConfigureAwait(false);
            foreach (var t in tries.Where(t => t.IsCompletedSuccessfully)) if (t.Result == c.Address) return c;
            return c.Using(answered[0]);
        }
        return null;
    }

    /// <summary>
    /// The same connection, encrypted if the server can (asked once over plain HTTP, without the code: trust on
    /// first use). A scanned QR code already carries the certificate; an older server has none.
    /// </summary>
    public static async Task<ConnectionInfo> SecureAsync(ConnectionInfo c, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        if (c.Secure) return c;
        try
        {
            using var http = new HttpClient(handler ?? Handler(null)) { Timeout = TimeSpan.FromSeconds(8) };
            using var res = await http.GetAsync(new Uri(c.PlainUri, Routes.Tls), ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return c;
            var tls = JsonSerializer.Deserialize(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false), RemoteJson.Default.TlsInfo);
            if (tls is not { Port: > 0, Fingerprint.Length: >= 40 }) return c;
            var secure = c with { TlsPort = tls.Port, Fingerprint = tls.Fingerprint };
            // Only once the encrypted port answers with that certificate (a firewall may let one port through and not the other).
            using var check = new HttpClient(handler ?? Handler(secure.Fingerprint)) { Timeout = TimeSpan.FromSeconds(8) };
            using var ok = await check.GetAsync(new Uri(secure.BaseUri, Routes.Tls), ct).ConfigureAwait(false);
            return ok.IsSuccessStatusCode ? secure : c;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return c;
        }
    }

    static readonly TimeSpan Quick = TimeSpan.FromSeconds(15), Slow = TimeSpan.FromMinutes(4);

    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? body, TimeSpan timeout, CancellationToken ct, HttpCompletionOption read = HttpCompletionOption.ResponseContentRead)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        HttpResponseMessage res;
        try
        {
            using var req = new HttpRequestMessage(method, url) { Content = body };
            res = await _http.SendAsync(req, read, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new RemoteError(0, $"The computer at {Name} didn't answer in time. Is it on, and the server running?", e);
        }
        catch (HttpRequestException e) when (e.InnerException is System.Security.Authentication.AuthenticationException)
        {
            throw new RemoteError(495, $"The computer at {Name} has a different security certificate than the one this phone knows. If PhotonCanvas was reinstalled or its remote settings reset, scan the QR code in Remote access again.", e);
        }
        catch (HttpRequestException e)
        {
            throw new RemoteError(0, $"Can't reach the computer at {Name}. Is the phone on the same network, and the server running (PhotonCanvas → Machine → Remote access)?", e);
        }
        if (res.IsSuccessStatusCode) return res;
        using (res)
        {
            string message = $"The server answered {(int)res.StatusCode}.";
            try
            {
                var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (JsonSerializer.Deserialize(text, RemoteJson.Default.ErrorBody)?.Error is { Length: > 0 } e) message = e;
            }
            catch (JsonException) { }
            throw new RemoteError((int)res.StatusCode, message);
        }
    }

    async Task<T> GetJsonAsync<T>(string url, JsonTypeInfo<T> type, TimeSpan timeout, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Get, url, null, timeout, ct).ConfigureAwait(false);
        return await ReadAsync(res, type, ct).ConfigureAwait(false);
    }

    async Task<T> PostJsonAsync<T>(string url, HttpContent? body, JsonTypeInfo<T> type, TimeSpan timeout, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Post, url, body ?? new StringContent(""), timeout, ct).ConfigureAwait(false);
        return await ReadAsync(res, type, ct).ConfigureAwait(false);
    }

    static async Task<T> ReadAsync<T>(HttpResponseMessage res, JsonTypeInfo<T> type, CancellationToken ct)
    {
        try
        {
            await using var s = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(s, type, ct).ConfigureAwait(false)
                ?? throw new RemoteError((int)res.StatusCode, "The server sent nothing back.");
        }
        catch (JsonException e)
        {
            throw new RemoteError((int)res.StatusCode, "The server's answer didn't make sense. Is it an older PhotonCanvas? Update it.", e);
        }
    }

    async Task<byte[]> GetBytesAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Get, url, null, timeout, ct).ConfigureAwait(false);
        return await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    static string WithWidth(string url, int width) => width > 0 ? $"{url}{(url.Contains('?') ? '&' : '?')}width={width}" : url;

    public Task<ServerInfo> InfoAsync(CancellationToken ct = default) => GetJsonAsync(Routes.Info, RemoteJson.Default.ServerInfo, Quick, ct);

    public Task<MachineStatus> StatusAsync(CancellationToken ct = default) => GetJsonAsync(Routes.Status, RemoteJson.Default.MachineStatus, Quick, ct);

    public async IAsyncEnumerable<RemoteEvent> EventsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        using var res = await SendAsync(HttpMethod.Get, Routes.Events, null, Quick, ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await foreach (var (kind, data) in ServerSentEvents.ReadAsync(reader, TimeSpan.FromSeconds(45), ct).ConfigureAwait(false))
            if (Parse(kind, data) is { } e) yield return e;
    }

    /// <summary>One server-sent event as a <see cref="RemoteEvent"/>; null for kinds this app doesn't know (a newer server).</summary>
    public static RemoteEvent? Parse(string kind, string data)
    {
        try
        {
            return kind switch
            {
                "status" => JsonSerializer.Deserialize(data, RemoteJson.Default.MachineStatus) is { } s ? new StatusChanged(s) : null,
                "job" => JsonSerializer.Deserialize(data, RemoteJson.Default.JobEvent) is { } j ? new JobChanged(j) : null,
                "alert" => JsonSerializer.Deserialize(data, RemoteJson.Default.AlertEvent) is { } a ? new AlarmRaised(a) : null,
                _ => null,
            };
        }
        catch (JsonException) { return null; }
    }

    public async Task<TakenPicture> TakePictureAsync(string camera, int width, CancellationToken ct = default)
    {
        using var res = await SendAsync(HttpMethod.Get, WithWidth(Routes.Camera(camera), width), null, Quick + Quick, ct).ConfigureAwait(false);
        var jpeg = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        string? Header(string name) => res.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;
        var at = DateTimeOffset.TryParse(Header("X-Taken-At"), out var t) ? t : DateTimeOffset.Now;
        return new TakenPicture(jpeg, Header("X-Picture-Id"), at, Header("X-Camera-Blanked") == "true");
    }

    public Task<byte[]> ImageAsync(string url, int width, CancellationToken ct = default) => GetBytesAsync(WithWidth(url, width), Slow, ct);

    public async Task<IReadOnlyList<ProjectItem>> ProjectsAsync(CancellationToken ct = default) =>
        await GetJsonAsync(Routes.Projects, RemoteJson.Default.ListProjectItem, Quick + Quick, ct).ConfigureAwait(false);

    public Task<CheckResult> CheckAsync(string projectId, CancellationToken ct = default) =>
        PostJsonAsync(Routes.Check(projectId), null, RemoteJson.Default.CheckResult, Slow, ct);

    public Task<LoadResult> LoadAsync(string projectId, CancellationToken ct = default) =>
        PostJsonAsync(Routes.Load(projectId), null, RemoteJson.Default.LoadResult, Slow, ct);

    public Task<ModeResult> StartAsync(string pictureId, CancellationToken ct = default) =>
        PostJsonAsync(Routes.Start, JsonContent(new StartRequest(pictureId)), RemoteJson.Default.ModeResult, TimeSpan.FromSeconds(30), ct);

    public Task<ModeResult> ControlAsync(string action, CancellationToken ct = default) =>
        PostJsonAsync(Routes.Control(action), null, RemoteJson.Default.ModeResult, TimeSpan.FromSeconds(30), ct);

    public async Task<IReadOnlyList<HistoryItem>> HistoryAsync(int max, CancellationToken ct = default) =>
        await GetJsonAsync($"{Routes.History}?max={max}", RemoteJson.Default.ListHistoryItem, Quick, ct).ConfigureAwait(false);

    static StringContent JsonContent(StartRequest r) => new(JsonSerializer.Serialize(r, RemoteJson.Default.StartRequest), Encoding.UTF8, "application/json");

    public void Dispose() => _http.Dispose();
}

/// <summary>Certificate fingerprints as the QR code carries them: SHA-256 of the certificate, base64url (43 characters).</summary>
public static class Fingerprints
{
    public static string Of(byte[] certificate) => System.Buffers.Text.Base64Url.EncodeToString(System.Security.Cryptography.SHA256.HashData(certificate));
}

/// <summary>Reads server-sent events: "event:" and "data:" lines, a blank line ends each, ":" lines are comments.</summary>
public static class ServerSentEvents
{
    /// <summary>The events in order. Ends when the stream does; throws <see cref="RemoteError"/> when nothing (not even a comment) arrives for <paramref name="silence"/>.</summary>
    public static async IAsyncEnumerable<(string Kind, string Data)> ReadAsync(TextReader reader, TimeSpan silence, [EnumeratorCancellation] CancellationToken ct = default)
    {
        string kind = "message";
        var data = new StringBuilder();
        while (true)
        {
            string? line;
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                limit.CancelAfter(silence);
                try { line = await reader.ReadLineAsync(limit.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new RemoteError(0, "The connection to the computer went quiet.");
                }
                catch (IOException e)
                {
                    throw new RemoteError(0, "The connection to the computer dropped.", e);
                }
            }
            if (line is null) yield break;
            if (line.Length == 0)
            {
                if (data.Length > 0) yield return (kind, data.ToString());
                kind = "message";
                data.Clear();
            }
            else if (line[0] == ':') continue;
            else if (line.StartsWith("event:", StringComparison.Ordinal)) kind = line[6..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart(' '));
            }
        }
    }
}
