using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace YTool.Device;

/// <summary>Tunables for <see cref="DeviceClient"/>. The defaults match the stock app and the verified Python client.</summary>
public sealed record DeviceClientOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary><c>/v1/user/ping</c> period.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>With no inbound data for this long the link is considered dead.</summary>
    public TimeSpan DeadLinkTimeout { get; init; } = TimeSpan.FromSeconds(11);
    /// <summary>How long to wait for the machine's button during pairing.</summary>
    public TimeSpan ButtonTimeout { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan ButtonPollInterval { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>How often to re-check the machine's mode while waiting for it to change.</summary>
    public TimeSpan ModePollInterval { get; init; } = TimeSpan.FromMilliseconds(300);
    /// <summary>A file transfer fails when the device goes quiet for this long.</summary>
    public TimeSpan TransferTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan SnapshotTimeout { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>
    /// <c>userID</c> sent when pairing. Firmware 40.54 drops the connection for a random UUID,
    /// so the stock app's guest id is the default.
    /// </summary>
    public string UserId { get; init; } = "mk-guest";
    /// <summary>Pairing key from <see cref="MachineKeys"/> (supplied by the user, not built in).</summary>
    public string PairingKey { get; init; } = "";
    /// <summary>Token cache file; null means <see cref="TokenStore.DefaultPath"/>.</summary>
    public string? StatePath { get; init; }
    /// <summary>Timezone name sent when pairing; null means the local zone's current name.</summary>
    public string? TimeZoneName { get; init; }
    /// <summary>Diagnostic messages. Called from thread-pool threads.</summary>
    public Action<string>? Log { get; init; }
    /// <summary>Also log every frame sent and received (needs <see cref="Log"/>).</summary>
    public bool TraceFrames { get; init; }

    /// <summary>Test hook: replaces the TLS WebSocket connect.</summary>
    internal Func<Uri, CancellationToken, Task<WebSocket>>? WebSocketFactory { get; init; }
}

/// <summary>
/// V2 protocol client for xTool F-series devices (<c>wss://&lt;ip&gt;:28900/websocket</c>).
/// See docs/protocol-f2-ultra.md.
/// <para>
/// Threading: all members are safe to call concurrently. <see cref="EventReceived"/> is raised on a
/// thread-pool thread, one event at a time, in arrival order; <see cref="Disconnected"/> is raised once
/// on a thread-pool thread; the pairing button callback runs on the caller of
/// <see cref="ConnectAsync"/>/<see cref="PairAsync"/>; upload progress is reported from the socket
/// reader thread (use <see cref="Progress{T}"/> to marshal to a UI thread). Handlers must not block.
/// </para>
/// <para>
/// A client connects once. After <see cref="Disconnected"/>, or if <see cref="ConnectAsync"/> throws,
/// dispose it and create a new one.
/// </para>
/// </summary>
public sealed class DeviceClient : IAsyncDisposable
{
    public const int Port = 28900;
    public const int PingTransactionId = 65510;
    public const int MaxTransactionId = 65500;

    const byte FileRequest = 0x01;
    const byte FileData = 0x81;
    const int PacketSize = 1024 * 1024;
    const int DownloadWindow = 5 * 1024 * 1024;
    const int JobFileType = 1;
    const int FirmwareFileType = 2;
    const int ImageFileType = 5;
    const string FinishUrl = "/v1/filetransfer/finish";

    const int StateNew = 0, StateConnecting = 1, StateConnected = 2, StateDisconnected = 3, StateDisposed = 4;

    readonly DeviceClientOptions _options;
    readonly CancellationTokenSource _lifetime = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<DeviceResponse>> _pending = new();
    readonly ConcurrentDictionary<int, UploadState> _uploads = new();
    readonly ConcurrentDictionary<int, DownloadState> _downloads = new();
    readonly Channel<DeviceEvent> _events = Channel.CreateUnbounded<DeviceEvent>(new() { SingleReader = true });
    readonly SemaphoreSlim _fileOpenLock = new(1, 1);
    readonly SemaphoreSlim _transferLock = new(1, 1);
    readonly Lock _txLock = new();
    int _txid;
    int _state;
    long _lastInbound;
    FramedSocket? _main;
    FramedSocket? _file;
    Task? _heartbeat;
    Task? _dispatcher;
    volatile string? _sessionToken;

    public DeviceClient(string ip, DeviceClientOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ip);
        Ip = ip.Trim();
        _options = options ?? new DeviceClientOptions();
    }

    public string Ip { get; }

    /// <summary>Shared by all channels of this connection; the device pairs the sockets by it.</summary>
    public string ConnectionId { get; } = Guid.NewGuid().ToString();

    public bool IsConnected => Volatile.Read(ref _state) == StateConnected;

    /// <summary>Session token from pairing (fw 40.54 with mk-guest returns none).</summary>
    public string? SessionToken => _sessionToken;

    /// <summary>Push messages from the device (mode changes, work events, button, lid...).</summary>
    public event Action<DeviceEvent>? EventReceived;

    /// <summary>The link was lost (socket closed or no traffic for <see cref="DeviceClientOptions.DeadLinkTimeout"/>). Not raised by <see cref="DisposeAsync"/>.</summary>
    public event Action<DeviceException>? Disconnected;

    string StatePath => _options.StatePath ?? TokenStore.DefaultPath;

    Uri ChannelUri(string function) => new($"wss://{Ip}:{Port}/websocket?id={ConnectionId}&function={function}");

    // ---- connection -------------------------------------------------------------------------

    /// <summary>
    /// Opens the instruction channel, starts the heartbeat and (by default) pairs.
    /// <paramref name="onButtonPressRequired"/> is called if the machine wants its button pressed.
    /// </summary>
    public async Task ConnectAsync(bool pair = true, Action? onButtonPressRequired = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _state, StateConnecting, StateNew) != StateNew)
            throw new InvalidOperationException("ConnectAsync can only be called once; create a new DeviceClient to reconnect.");
        try
        {
            _main = await OpenChannelAsync("instruction", OnMainFrameAsync, OnMainClosed, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _lastInbound, Environment.TickCount64);
            if (Interlocked.CompareExchange(ref _state, StateConnected, StateConnecting) != StateConnecting)
                throw new DeviceException(DeviceException.NotConnectedCode, "The connection was closed while connecting.");
            _dispatcher = Task.Run(DispatchLoopAsync, CancellationToken.None);
            _heartbeat = Task.Run(() => HeartbeatLoopAsync(_lifetime.Token), CancellationToken.None);
            Log($"connected to {Ip} (id {ConnectionId})");
            if (pair)
                await PairAsync(onButtonPressRequired, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await ShutdownAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _state, StateDisposed) == StateDisposed)
            return;
        await ShutdownAsync().ConfigureAwait(false);
    }

    async Task ShutdownAsync()
    {
        Interlocked.CompareExchange(ref _state, StateDisconnected, StateConnecting);
        Interlocked.CompareExchange(ref _state, StateDisconnected, StateConnected);
        _lifetime.Cancel();
        FailAll(new DeviceException(DeviceException.NotConnectedCode, $"The connection to {Ip} was closed."));
        _events.Writer.TryComplete();
        var main = Interlocked.Exchange(ref _main, null);
        var file = Interlocked.Exchange(ref _file, null);
        if (file is not null)
            await file.CloseAsync().ConfigureAwait(false);
        if (main is not null)
            await main.CloseAsync().ConfigureAwait(false);
        if (_heartbeat is { } hb)
            await Task.WhenAny(hb, Task.Delay(2000)).ConfigureAwait(false);
    }

    void MarkDisconnected(Exception reason)
    {
        if (Interlocked.CompareExchange(ref _state, StateDisconnected, StateConnected) != StateConnected)
            return;
        var error = reason as DeviceException
            ?? new DeviceException(DeviceException.DisconnectedCode, $"Lost the connection to the machine at {Ip}: {reason.Message}", inner: reason);
        Log($"disconnected: {error.Message}");
        _lifetime.Cancel();
        FailAll(error);
        _events.Writer.TryComplete();
        _main?.Abort();
        _file?.Abort();
        _ = Task.Run(() =>
        {
            try
            {
                Disconnected?.Invoke(error);
            }
            catch (Exception e)
            {
                Log($"Disconnected handler threw: {e}");
            }
        });
    }

    void FailAll(DeviceException error)
    {
        foreach (var txid in _pending.Keys)
            if (_pending.TryRemove(txid, out var tcs))
                tcs.TrySetException(error);
        FailTransfers(error);
    }

    void FailTransfers(DeviceException error)
    {
        foreach (var up in _uploads.Values)
            up.Fail(error);
        foreach (var dl in _downloads.Values)
            dl.Fail(error);
    }

    async Task<FramedSocket> OpenChannelAsync(
        string function,
        Func<FramedSocket, DecodedFrame, ValueTask> onFrame,
        Action<FramedSocket, Exception> onClosed,
        CancellationToken cancellationToken)
    {
        var uri = ChannelUri(function);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cts.CancelAfter(_options.ConnectTimeout);
        WebSocket ws;
        try
        {
            if (_options.WebSocketFactory is { } factory)
            {
                ws = await factory(uri, cts.Token).ConfigureAwait(false);
            }
            else
            {
                var client = new ClientWebSocket();
                // The device serves a self-signed certificate. This callback applies to this socket only.
                client.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                client.Options.KeepAliveInterval = TimeSpan.Zero; // we run our own heartbeat
                client.Options.Proxy = null; // LAN device: never go through a system proxy
                try
                {
                    await client.ConnectAsync(uri, cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
                ws = client;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (_lifetime.IsCancellationRequested)
                throw new DeviceException(DeviceException.NotConnectedCode, $"The connection to {Ip} was closed.");
            throw new DeviceException(DeviceException.TimeoutCode,
                $"No answer from {uri.GetLeftPart(UriPartial.Path)} within {Seconds(_options.ConnectTimeout)}. Check that the machine is on and on this network.");
        }
        catch (Exception e) when (e is WebSocketException or IOException or HttpRequestException)
        {
            throw new DeviceException(DeviceException.NotConnectedCode,
                $"Could not connect to {uri.GetLeftPart(UriPartial.Path)} ({function}): {e.Message}. " +
                "The machine may be off, on another network, or using the older V1 protocol.", inner: e);
        }
        var skip = function == "media_stream" ? new[] { ProtocolType.MediaStream } : null;
        var socket = new FramedSocket(ws, function, new FrameDecoder(skip), onFrame, onClosed, OnInbound, _options);
        socket.Start(_lifetime.Token);
        return socket;
    }

    void OnInbound() => Volatile.Write(ref _lastInbound, Environment.TickCount64);

    void OnMainClosed(FramedSocket socket, Exception reason) => MarkDisconnected(reason);

    void OnFileClosed(FramedSocket socket, Exception reason)
    {
        if (!IsConnected)
            return;
        // Losing the file channel fails running transfers; the next transfer reopens it.
        if (Interlocked.CompareExchange(ref _file, null, socket) == socket)
        {
            Log($"file channel closed: {reason.Message}");
            socket.Abort();
            FailTransfers(new DeviceException(DeviceException.DisconnectedCode, $"The file channel to {Ip} closed: {reason.Message}", inner: reason));
        }
    }

    async Task<FramedSocket> EnsureFileChannelAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _file) is { } existing)
            return existing;
        await _fileOpenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfNotConnected();
            if (_file is { } f)
                return f;
            var socket = await OpenChannelAsync("file_stream", OnFileFrameAsync, OnFileClosed, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _file, socket);
            return socket;
        }
        finally
        {
            _fileOpenLock.Release();
        }
    }

    async Task HeartbeatLoopAsync(CancellationToken token)
    {
        long intervalMs = (long)_options.HeartbeatInterval.TotalMilliseconds;
        long deadMs = (long)_options.DeadLinkTimeout.TotalMilliseconds;
        var tick = TimeSpan.FromMilliseconds(Math.Clamp(Math.Min(intervalMs, deadMs / 4), 10, 500));
        long nextPing = Environment.TickCount64 + intervalMs;
        try
        {
            while (true)
            {
                await Task.Delay(tick, token).ConfigureAwait(false);
                long now = Environment.TickCount64;
                long quiet = now - Volatile.Read(ref _lastInbound);
                if (quiet > deadMs)
                {
                    MarkDisconnected(new DeviceException(DeviceException.DisconnectedCode,
                        $"No data from the machine at {Ip} for {Seconds(TimeSpan.FromMilliseconds(quiet))}; the connection is lost."));
                    return;
                }
                if (now < nextPing || _main is not { } main)
                    continue;
                nextPing = now + intervalMs;
                var ping = new JsonObject
                {
                    ["type"] = "request",
                    ["method"] = "GET",
                    ["url"] = "/v1/user/ping",
                    ["transactionId"] = PingTransactionId,
                    ["data"] = new JsonObject(),
                    ["params"] = new JsonObject(),
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                };
                try
                {
                    await main.SendAsync(Frame.Encode(ping.ToJsonString()), token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    MarkDisconnected(e);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    async Task DispatchLoopAsync()
    {
        await foreach (var ev in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                EventReceived?.Invoke(ev);
            }
            catch (Exception e)
            {
                Log($"EventReceived handler threw: {e}");
            }
        }
    }

    // ---- frames -------------------------------------------------------------------------------

    ValueTask OnMainFrameAsync(FramedSocket socket, DecodedFrame frame)
    {
        if (frame.Type != ProtocolType.Json)
        {
            Log($"ignored {frame.Type} frame ({frame.Payload.Length} B) on instruction channel");
            return ValueTask.CompletedTask;
        }
        if (Json.TryParseObject(frame.Payload) is not { } msg)
        {
            Log($"unparsable JSON frame ({frame.Payload.Length} B)");
            return ValueTask.CompletedTask;
        }
        HandleMainMessage(msg);
        return ValueTask.CompletedTask;
    }

    internal void HandleMainMessage(JsonObject msg)
    {
        bool isResponse = Json.Str(msg, "type") == "response";
        if (isResponse && DeviceResponse.TryParse(msg) is { } resp)
        {
            if (resp.TransactionId == PingTransactionId)
                return;
            if (_pending.TryRemove(resp.TransactionId, out var tcs))
            {
                tcs.TrySetResult(resp);
                return;
            }
        }
        if (Json.Str(msg, "url") == FinishUrl)
        {
            OnTransferFinish(msg);
            return;
        }
        if (isResponse)
        {
            Log($"unmatched response: {msg.ToJsonString()}");
            return;
        }
        _events.Writer.TryWrite(DeviceEvent.Parse(msg));
    }

    void OnTransferFinish(JsonObject msg)
    {
        var data = msg["data"] as JsonObject ?? [];
        if (Json.Int(data, "channel") is int ch && _uploads.TryGetValue(ch, out var up))
            up.Finish(data);
        else
            Log($"transfer finish for no active upload: {msg.ToJsonString()}");
    }

    async ValueTask OnFileFrameAsync(FramedSocket socket, DecodedFrame frame)
    {
        if (frame.Type == ProtocolType.Json)
        {
            if (Json.TryParseObject(frame.Payload) is { } msg && Json.Str(msg, "url") == FinishUrl)
                OnTransferFinish(msg);
            return;
        }
        var p = frame.Payload;
        if (frame.Type != ProtocolType.FileTransfer || p.Length == 0)
            return;
        if (p[0] == FileRequest && p.Length >= 10)
        {
            int ch = p[1];
            long offset = ReadUInt40(p.AsSpan(2));
            int window = (p[7] << 16) | (p[8] << 8) | p[9];
            if (_uploads.TryGetValue(ch, out var up))
                await ServeWindowAsync(socket, up, ch, offset, window).ConfigureAwait(false);
            else
                Log($"window request for channel {ch} with no active upload");
        }
        else if (p[0] == FileData && p.Length >= 7)
        {
            int ch = p[1];
            long offset = ReadUInt40(p.AsSpan(2));
            if (_downloads.TryGetValue(ch, out var dl))
                dl.Write(offset, p.AsSpan(7));
        }
    }

    async Task ServeWindowAsync(FramedSocket socket, UploadState up, int ch, long offset, int window)
    {
        up.Touch();
        long total = up.Data.Length;
        long end = Math.Min(offset + window, total);
        try
        {
            for (long pos = offset; pos < end;)
            {
                int n = (int)Math.Min(up.PacketSize, end - pos);
                var pkt = new byte[7 + n];
                pkt[0] = FileData;
                pkt[1] = (byte)ch;
                WriteUInt40(pkt.AsSpan(2), pos);
                up.Data.AsSpan((int)pos, n).CopyTo(pkt.AsSpan(7));
                await socket.SendAsync(Frame.Encode(pkt, ProtocolType.FileTransfer, bodyCrc: false), _lifetime.Token).ConfigureAwait(false);
                pos += n;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            up.Fail(new DeviceException(DeviceException.DisconnectedCode, $"Sending file data failed: {e.Message}", inner: e));
            return;
        }
        up.ReportSent(end);
    }

    static long ReadUInt40(ReadOnlySpan<byte> b) =>
        ((long)b[0] << 32) | ((long)b[1] << 24) | ((long)b[2] << 16) | ((long)b[3] << 8) | b[4];

    static void WriteUInt40(Span<byte> b, long v)
    {
        b[0] = (byte)(v >> 32);
        b[1] = (byte)(v >> 24);
        b[2] = (byte)(v >> 16);
        b[3] = (byte)(v >> 8);
        b[4] = (byte)v;
    }

    // ---- requests -----------------------------------------------------------------------------

    internal static string BuildRequest(string method, string url, JsonNode? data, JsonNode? parameters, int transactionId, string? sessionToken, long timestamp)
    {
        var msg = new JsonObject
        {
            ["type"] = "request",
            ["method"] = method,
            ["url"] = url,
            ["params"] = parameters?.DeepClone() ?? new JsonObject(),
            ["data"] = data?.DeepClone() ?? new JsonObject(),
            ["timestamp"] = timestamp,
            ["transactionId"] = transactionId,
        };
        if (!string.IsNullOrEmpty(sessionToken))
            msg["sessionToken"] = sessionToken;
        return msg.ToJsonString();
    }

    int NextTransactionId()
    {
        lock (_txLock)
        {
            do
                _txid = _txid % MaxTransactionId + 1;
            while (_pending.ContainsKey(_txid));
            return _txid;
        }
    }

    void ThrowIfNotConnected()
    {
        if (!IsConnected)
            throw new DeviceException(DeviceException.NotConnectedCode, $"Not connected to the machine at {Ip}.");
    }

    /// <summary>Sends a request and returns the raw response, whatever its code.</summary>
    public async Task<DeviceResponse> SendRequestAsync(
        string method,
        string url,
        JsonNode? data = null,
        JsonNode? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ThrowIfNotConnected();
        var main = _main ?? throw new DeviceException(DeviceException.NotConnectedCode, $"Not connected to the machine at {Ip}.");
        int txid = NextTransactionId();
        var tcs = new TaskCompletionSource<DeviceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[txid] = tcs;
        try
        {
            if (!IsConnected)
                ThrowIfNotConnected();
            string json = BuildRequest(method, url, data, parameters, txid, _sessionToken, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            try
            {
                await main.SendAsync(Frame.Encode(json), _lifetime.Token, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DeviceException(DeviceException.NotConnectedCode, $"The connection to {Ip} was closed.");
            }
            catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException)
            {
                MarkDisconnected(e);
                throw new DeviceException(DeviceException.DisconnectedCode, $"Lost the connection to the machine at {Ip}: {e.Message}", inner: e);
            }
            var limit = timeout ?? _options.RequestTimeout;
            try
            {
                return await tcs.Task.WaitAsync(limit, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new DeviceException(DeviceException.TimeoutCode, $"{method} {url}: no reply from the machine within {Seconds(limit)}.");
            }
        }
        finally
        {
            _pending.TryRemove(txid, out _);
        }
    }

    /// <summary>Sends a request; throws <see cref="DeviceException"/> unless the code is 0. Returns the response <c>data</c>.</summary>
    public async Task<JsonNode?> RequestAsync(
        string method,
        string url,
        JsonNode? data = null,
        JsonNode? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var resp = await SendRequestAsync(method, url, data, parameters, timeout, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccess)
            throw DeviceException.FromResponse(resp.Code, resp.Message, url, resp.Data);
        return resp.Data;
    }

    public Task<JsonNode?> GetAsync(string url, JsonNode? data = null, JsonNode? parameters = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        RequestAsync("GET", url, data, parameters, timeout, cancellationToken);

    static JsonObject AsObject(JsonNode? node, string url) =>
        node as JsonObject ?? throw new DeviceException(DeviceException.ProtocolErrorCode, $"{url}: the machine's reply had no data.");

    // ---- pairing ------------------------------------------------------------------------------

    /// <summary>
    /// <c>/v1/user/parity</c> with the cached token. On 406 calls <paramref name="onButtonPressRequired"/> and polls
    /// <c>/v1/user/auth-status</c> until the button is pressed or <see cref="DeviceClientOptions.ButtonTimeout"/>.
    /// Stores the returned token. Returns the response data.
    /// </summary>
    public async Task<JsonObject> PairAsync(Action? onButtonPressRequired = null, CancellationToken cancellationToken = default)
    {
        const string url = "/v1/user/parity";
        if (string.IsNullOrEmpty(_options.PairingKey))
            throw new DeviceException(DeviceException.NotConnectedCode, "The machine keys haven't been set up yet (Machine tab → Connection keys).");
        string token = TokenStore.GetToken(StatePath, Ip) ?? "";
        var resp = await SendRequestAsync("GET", url, new JsonObject
        {
            ["userID"] = _options.UserId,
            ["userKey"] = _options.PairingKey,
            ["timezone"] = _options.TimeZoneName ?? LocalTimeZoneName(),
            ["token"] = token,
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (resp.Code == 406)
        {
            Log("waiting for the button on the machine");
            onButtonPressRequired?.Invoke();
            var deadline = Environment.TickCount64 + (long)_options.ButtonTimeout.TotalMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                await Task.Delay(_options.ButtonPollInterval, cancellationToken).ConfigureAwait(false);
                try
                {
                    resp = await SendRequestAsync("GET", "/v1/user/auth-status", timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (DeviceException e) when (e.Code == DeviceException.TimeoutCode)
                {
                    continue;
                }
                if (resp.Code is 0 or 410 or 409 or 503)
                    break;
            }
            if (resp.Code == 406)
                throw new DeviceException(410, $"{url}: the button on the machine was not pressed within {Seconds(_options.ButtonTimeout)}.");
        }
        if (!resp.IsSuccess)
            throw DeviceException.FromResponse(resp.Code, resp.Message ?? "pairing failed", url, resp.Data);

        var data = resp.Data as JsonObject ?? [];
        _sessionToken = Json.Str(data, "sessionToken");
        if (Json.Str(data, "token") is { } newToken)
        {
            try
            {
                TokenStore.SaveToken(StatePath, Ip, newToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log($"could not save the pairing token: {e.Message}");
            }
        }
        Log($"paired: {resp.Message}");
        return data;
    }

    static string LocalTimeZoneName()
    {
        var tz = TimeZoneInfo.Local;
        string name = tz.IsDaylightSavingTime(DateTime.Now) ? tz.DaylightName : tz.StandardName;
        return string.IsNullOrWhiteSpace(name) ? tz.Id : name;
    }

    // ---- file transfer ------------------------------------------------------------------------

    /// <summary>
    /// Device-driven upload: announces the file with its MD5, then serves the windows the device
    /// requests on the file channel until it reports <c>/v1/filetransfer/finish</c>.
    /// Firmware (file type 2) is refused. Returns the finish data.
    /// </summary>
    public async Task<JsonObject> UploadFileAsync(
        byte[] data,
        string filename,
        int fileType = JobFileType,
        int channel = 1,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        if (fileType == FirmwareFileType)
            throw new ArgumentException("Firmware upload is not supported.", nameof(fileType));
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, 255);

        await _transferLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureFileChannelAsync(cancellationToken).ConfigureAwait(false);
            var up = new UploadState(data, progress);
            _uploads[channel] = up;
            try
            {
                var info = await RequestAsync("PUT", "/v1/filetransfer/upload", new JsonObject
                {
                    ["filetype"] = fileType,
                    ["filename"] = filename,
                    ["filesize"] = data.Length,
                    ["digesttype"] = 1,
                    ["digestdata"] = Convert.ToHexStringLower(MD5.HashData(data)),
                    ["channel"] = channel,
                    ["packetsize"] = PacketSize,
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
                long devicePacket = Json.Long(info as JsonObject, "packetsize") ?? PacketSize;
                up.PacketSize = (int)Math.Clamp(devicePacket, 1, PacketSize);

                while (!up.Done.Task.IsCompleted)
                {
                    if (!await up.Activity.WaitAsync(_options.TransferTimeout, cancellationToken).ConfigureAwait(false))
                        throw new DeviceException(DeviceException.TimeoutCode,
                            $"Upload of {filename}: the machine stopped asking for data ({up.Sent} of {data.Length} bytes sent).");
                }
                var result = await up.Done.Task.ConfigureAwait(false);
                int code = Json.Int(result, "code") ?? DeviceException.ProtocolErrorCode;
                if (code != 0)
                    throw new DeviceException(code, $"Upload of {filename} failed: {Json.Str(result, "message") ?? $"error {code}"}.", result);
                progress?.Report(1.0);
                return result;
            }
            finally
            {
                _uploads.TryRemove(new KeyValuePair<int, UploadState>(channel, up));
            }
        }
        finally
        {
            _transferLock.Release();
        }
    }

    /// <summary>Downloads a file from the device in 5 MB windows and checks its MD5.</summary>
    public async Task<byte[]> DownloadFileAsync(string filename, int fileType, int channel = 1, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, 255);

        await _transferLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureFileChannelAsync(cancellationToken).ConfigureAwait(false);
            const string url = "/v1/filetransfer/download";
            var info = AsObject(await RequestAsync("PUT", url, new JsonObject
            {
                ["filetype"] = fileType,
                ["filename"] = filename,
                ["digesttype"] = 1,
                ["channel"] = channel,
                ["packetsize"] = PacketSize,
            }, cancellationToken: cancellationToken).ConfigureAwait(false), url);
            long size = Json.Long(info, "filesize")
                ?? throw new DeviceException(DeviceException.ProtocolErrorCode, $"{url}: the machine did not say how big {filename} is.", info);
            if (size > Array.MaxLength)
                throw new DeviceException(DeviceException.ProtocolErrorCode, $"{filename} is too large ({size} bytes).", info);

            var dl = new DownloadState((int)size);
            _downloads[channel] = dl;
            try
            {
                for (long offset = 0; offset < size;)
                {
                    int window = (int)Math.Min(DownloadWindow, size - offset);
                    dl.BeginWindow();
                    var req = new byte[10];
                    req[0] = FileRequest;
                    req[1] = (byte)channel;
                    WriteUInt40(req.AsSpan(2), offset);
                    req[7] = (byte)(window >> 16);
                    req[8] = (byte)(window >> 8);
                    req[9] = (byte)window;
                    await file.SendAsync(Frame.Encode(req, ProtocolType.FileTransfer, bodyCrc: false), _lifetime.Token, cancellationToken).ConfigureAwait(false);
                    while (dl.WindowReceived < window)
                    {
                        dl.ThrowIfFailed();
                        if (!await dl.Signal.WaitAsync(_options.TransferTimeout, cancellationToken).ConfigureAwait(false))
                            throw new DeviceException(DeviceException.TimeoutCode,
                                $"Download of {filename}: no data from the machine for {Seconds(_options.TransferTimeout)}.");
                    }
                    dl.ThrowIfFailed();
                    offset += window;
                }
                string? digest = Json.Str(info, "digestdata");
                if (digest is not null && !string.Equals(Convert.ToHexStringLower(MD5.HashData(dl.Buffer)), digest, StringComparison.OrdinalIgnoreCase))
                    throw new DeviceException(DeviceException.ChecksumMismatchCode, $"Download of {filename} was corrupted (MD5 mismatch).");
            }
            catch
            {
                await AbortTransferAsync(channel).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _downloads.TryRemove(new KeyValuePair<int, DownloadState>(channel, dl));
            }
            await SendRequestAsync("PUT", FinishUrl, new JsonObject
            {
                ["code"] = 0,
                ["message"] = "file transfer finish",
                ["channel"] = channel,
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return dl.Buffer;
        }
        finally
        {
            _transferLock.Release();
        }
    }

    async Task AbortTransferAsync(int channel)
    {
        if (!IsConnected)
            return;
        try
        {
            await SendRequestAsync("PUT", FinishUrl, new JsonObject
            {
                ["code"] = -9,
                ["message"] = "File transfer aborted",
                ["channel"] = channel,
            }, timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (DeviceException e)
        {
            Log($"abort of transfer on channel {channel} failed: {e.Message}");
        }
    }

    // ---- typed helpers (read-only, plus job upload without auto-start) -------------------------

    public async Task<MachineInfo> GetMachineInfoAsync(CancellationToken cancellationToken = default)
    {
        const string url = "/v1/device/machineInfo";
        return MachineInfo.Parse(AsObject(await GetAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false), url));
    }

    /// <summary>
    /// The machine's state, with the mode in the same words its MODE_CHANGE events use
    /// (<c>P_IDLE</c>, <c>P_WORK</c>, <c>P_WORKING</c>, <c>P_PAUSE</c>…; see <see cref="NormalizeMode"/>).
    /// </summary>
    public async Task<RuntimeState> GetRuntimeStateAsync(CancellationToken cancellationToken = default)
    {
        const string url = "/v1/device/runtime-infos";
        var data = AsObject(await GetAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false), url);
        var cur = data["curMode"] as JsonObject;
        string raw = Json.Str(cur, "mode")
            ?? throw new DeviceException(DeviceException.ProtocolErrorCode, $"{url}: the reply had no curMode.mode.", data);
        string? sub = Json.Str(cur, "subMode"), desc = Json.Str(cur, "desc");
        return new RuntimeState(NormalizeMode(raw, sub, desc), raw, sub, desc, Json.Str(cur, "taskId"));
    }

    /// <summary>The machine's mode, e.g. <c>P_IDLE</c>, <c>P_WORK</c>, <c>P_WORKING</c>.</summary>
    public async Task<string> GetRuntimeModeAsync(CancellationToken cancellationToken = default) =>
        (await GetRuntimeStateAsync(cancellationToken).ConfigureAwait(false)).Mode;

    /// <summary>
    /// runtime-infos reports a job as mode <c>Work</c> with a sub-mode (firmware 40.54:
    /// <c>{"mode":"Work","subMode":"workReady"}</c> while waiting for the button), where
    /// the events say <c>P_WORK</c>. This maps it to the event words, as xTool Studio does:
    /// workReady → P_WORK, working → P_WORKING, workPause → P_PAUSE, framing → P_FRAMING.
    /// </summary>
    public static string NormalizeMode(string mode, string? subMode, string? desc)
    {
        if (mode != "Work") return mode;
        if (desc is "frame" or "frameExt") return "P_FRAMING";
        return subMode switch
        {
            "workReady" => "P_WORK",
            "working" => "P_WORKING",
            "workPause" => "P_PAUSE",
            _ => $"Work/{subMode}", // unknown: treated as busy, never replaced
        };
    }

    /// <summary>Raw data of <c>/v1/processing/progress</c> (shape not yet documented).</summary>
    public Task<JsonNode?> GetProgressAsync(CancellationToken cancellationToken = default) =>
        GetAsync("/v1/processing/progress", cancellationToken: cancellationToken);

    /// <summary>Laser head Z in mm (material distance = z − 6 on the F2 Ultra).</summary>
    public async Task<double> GetHeadZAsync(CancellationToken cancellationToken = default)
    {
        const string url = "/v1/laser-head/focus/parameter";
        var data = AsObject(await GetAsync(url, new JsonObject { ["action"] = "coord" }, cancellationToken: cancellationToken).ConfigureAwait(false), url);
        return Json.Number(data["value"])
            ?? throw new DeviceException(DeviceException.ProtocolErrorCode, $"{url}: the reply had no value.", data);
    }

    /// <summary>"Enclosure-opened processing": true when the button may start jobs with the lid open.</summary>
    public async Task<bool> GetLidOpenProcessingAsync(CancellationToken cancellationToken = default)
    {
        const string url = "/v1/device/configs";
        var data = AsObject(await GetAsync(url, new JsonObject
        {
            ["alias"] = "config",
            ["type"] = "user",
            ["kv"] = new JsonArray("gapCheckWithKey"),
        }, cancellationToken: cancellationToken).ConfigureAwait(false), url);
        bool gapCheck = Json.Bool(data["gapCheckWithKey"])
            ?? throw new DeviceException(DeviceException.ProtocolErrorCode, $"{url}: the reply had no gapCheckWithKey.", data);
        return !gapCheck;
    }

    /// <summary>
    /// Auto-focus. On V2 firmware the machine measures the material and moves
    /// the head itself; we switch it to focus mode, start it, and wait for
    /// FOCUS_FINISHED (or FOCUS_STOPED, e.g. its button). Returns the new head Z.
    /// On any failure the machine is put back to idle.
    /// </summary>
    public async Task<double> AutoFocusAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var outcome = new TaskCompletionSource<DeviceEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEvent(DeviceEvent ev)
        {
            if (ev.Type is "FOCUS_FINISHED" or "FOCUS_STOPED" or "AXIS_HOME_STARTED") outcome.TrySetResult(ev);
        }
        EventReceived += OnEvent;
        try
        {
            await RequestAsync("PUT", "/v1/device/mode", new JsonObject { ["mode"] = "P_AUTOFOCUS" }, cancellationToken: cancellationToken).ConfigureAwait(false);
            await RequestAsync("POST", "/v1/laser-head/focus/control", new JsonObject { ["action"] = "auto_start" },
                timeout: TimeSpan.FromSeconds(60), cancellationToken: cancellationToken).ConfigureAwait(false);
            DeviceEvent ev;
            try
            {
                ev = await outcome.Task.WaitAsync(timeout ?? TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new DeviceException(DeviceException.FocusFailedCode, "Auto-focus didn't finish within 2 minutes.");
            }
            if (ev.Type != "FOCUS_FINISHED")
                throw new DeviceException(DeviceException.FocusFailedCode,
                    ev.Type == "AXIS_HOME_STARTED" ? "Auto-focus was interrupted: the machine started homing its Z axis."
                    : ev.InfoText == "Button" ? "Auto-focus was stopped with the machine's button."
                    : $"Auto-focus stopped{(string.IsNullOrEmpty(ev.InfoText) ? "" : $" ({ev.InfoText})")}.");
            return await GetHeadZAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is DeviceException or OperationCanceledException)
        {
            await ReturnToIdleAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            EventReceived -= OnEvent;
        }
    }

    /// <summary>Put the machine back to idle (how the stock app abandons focusing). Never throws.</summary>
    public async Task ReturnToIdleAsync()
    {
        if (!IsConnected) return;
        try
        {
            await SendRequestAsync("PUT", "/v1/device/mode", new JsonObject { ["mode"] = "P_IDLE" }, null, TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is DeviceException or OperationCanceledException or IOException or System.Net.WebSockets.WebSocketException)
        {
            // Best effort: the machine also returns to idle on its own after a stop.
        }
    }

    // ---- running job ---------------------------------------------------------------

    /// <summary>Pause the running job (the machine keeps it; resume with the button or <see cref="ResumeJobAsync"/>).</summary>
    public Task PauseJobAsync(CancellationToken cancellationToken = default) => ProcessingState("pause", cancellationToken);

    /// <summary>Continue a paused job.</summary>
    public Task ResumeJobAsync(CancellationToken cancellationToken = default) => ProcessingState("start", cancellationToken);

    /// <summary>Stop (cancel) the running or paused job.</summary>
    public Task StopJobAsync(CancellationToken cancellationToken = default) => ProcessingState("stop", cancellationToken);

    Task ProcessingState(string action, CancellationToken ct) =>
        RequestAsync("PUT", "/v1/processing/state", parameters: new JsonObject { ["action"] = action }, cancellationToken: ct);

    public async Task<JobProgress> GetJobProgressAsync(CancellationToken cancellationToken = default) =>
        JobProgress.Parse(await GetAsync("/v1/processing/progress", cancellationToken: cancellationToken).ConfigureAwait(false));

    // ---- camera fill lights ----------------------------------------------------------

    /// <summary>Machine brightness value (0, or 20–255) ↔ percent (0–100), as the stock app shows it.</summary>
    public static int LightToPercent(double raw) => raw <= 0 ? 0 : (int)Math.Clamp(Math.Round((raw - 20) * 99 / 235 + 1), 0, 100);
    public static int PercentToLight(int percent) => percent <= 0 ? 0 : (int)Math.Round((Math.Clamp(percent, 1, 100) - 1) * 235.0 / 99 + 20);

    /// <summary>Front and back fill-light brightness, percent.</summary>
    public async Task<(int Front, int Back)> GetFillLightsAsync(CancellationToken cancellationToken = default)
    {
        const string url = "/v1/device/configs";
        var data = AsObject(await GetAsync(url, new JsonObject
        {
            ["alias"] = "config",
            ["type"] = "user",
            ["kv"] = new JsonArray("fillLightBrightFront", "fillLightBrightBack"),
        }, cancellationToken: cancellationToken).ConfigureAwait(false), url);
        return (LightToPercent(Json.Number(data["fillLightBrightFront"]) ?? 0), LightToPercent(Json.Number(data["fillLightBrightBack"]) ?? 0));
    }

    /// <summary>Set the fill lights (percent; null leaves one unchanged). Saved on the machine and applied at once.</summary>
    public async Task SetFillLightsAsync(int? frontPercent, int? backPercent, CancellationToken cancellationToken = default)
    {
        var kv = new JsonObject();
        var now = new JsonObject { ["action"] = "set_bri" };
        if (frontPercent is { } f) { kv["fillLightBrightFront"] = PercentToLight(f); now["front"] = PercentToLight(f); }
        if (backPercent is { } b) { kv["fillLightBrightBack"] = PercentToLight(b); now["back"] = PercentToLight(b); }
        if (kv.Count == 0) return;
        await RequestAsync("PUT", "/v1/device/configs", new JsonObject { ["alias"] = "config", ["type"] = "user", ["kv"] = kv },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequestAsync("PUT", "/v1/peripheral/param", now, new JsonObject { ["type"] = "fill_light" },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether the machine's beeper sounds (button presses, job start/end). Stored on the machine.</summary>
    public async Task<bool> GetBeeperEnabledAsync(CancellationToken cancellationToken = default)
    {
        const string url = "/v1/device/configs";
        var data = AsObject(await GetAsync(url, new JsonObject
        {
            ["alias"] = "config",
            ["type"] = "user",
            ["kv"] = new JsonArray("beepEnable"),
        }, cancellationToken: cancellationToken).ConfigureAwait(false), url);
        return Json.Bool(data["beepEnable"])
            ?? throw new DeviceException(DeviceException.ProtocolErrorCode, $"{url}: the reply had no beepEnable.", data);
    }

    /// <summary>Turns the machine's beeper on or off (the firmware has no volume setting). Stored on the machine.</summary>
    public async Task SetBeeperEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await RequestAsync("PUT", "/v1/device/configs", new JsonObject
        {
            ["alias"] = "config",
            ["type"] = "user",
            ["kv"] = new JsonObject { ["beepEnable"] = enabled },
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns "Enclosure-opened processing" on or off. Stored on the machine.</summary>
    public async Task SetLidOpenProcessingAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await RequestAsync("PUT", "/v1/device/configs", new JsonObject
        {
            ["alias"] = "config",
            ["type"] = "user",
            ["kv"] = new JsonObject { ["gapCheckWithKey"] = !enabled },
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads an .xf job as <c>tmp.xf</c> and arms it with <c>autoStart: 0</c>: the machine then waits
    /// for its physical button. Refuses unless the machine is <c>P_IDLE</c>, except that with
    /// <paramref name="replaceLoadedJob"/> a job that is loaded but not started (<c>P_WORK</c>) is
    /// cancelled first (the same request xTool Studio sends; nothing moves). Returns the task id.
    /// </summary>
    public async Task<string> UploadJobAsync(byte[] xf, IProgress<double>? progress = null, CancellationToken cancellationToken = default, bool replaceLoadedJob = false)
    {
        ArgumentNullException.ThrowIfNull(xf);
        if (xf.Length == 0)
            throw new ArgumentException("The job file is empty.", nameof(xf));
        var state = await GetRuntimeStateAsync(cancellationToken).ConfigureAwait(false);
        Log($"Before upload the machine reports {state.RawMode}/{state.SubMode} (task {state.TaskId ?? "none"}).");
        string mode = state.Mode;
        if (mode == "P_WORK" && replaceLoadedJob)
        {
            Log("A job is already loaded and not started: cancelling it to make room.");
            await StopJobAsync(cancellationToken).ConfigureAwait(false);
            mode = await WaitForModeAsync("P_IDLE", TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
        }
        if (mode != "P_IDLE")
            throw new DeviceException(DeviceException.BusyCode, BusyMessage(mode));
        await UploadFileAsync(xf, "tmp.xf", JobFileType, 1, progress, cancellationToken).ConfigureAwait(false);
        string taskId = Guid.NewGuid().ToString();
        await RequestAsync("PUT", "/v1/processing/upload/config", new JsonObject
        {
            ["fileType"] = "xf",
            ["autoStart"] = 0, // never auto-start: the user starts jobs with the machine's button
            ["taskId"] = taskId,
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return taskId;
    }

    /// <summary>Polls the runtime mode until it is <paramref name="wanted"/> or the time runs out; returns the last mode seen.</summary>
    async Task<string> WaitForModeAsync(string wanted, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            string mode = await GetRuntimeModeAsync(cancellationToken).ConfigureAwait(false);
            if (mode == wanted || DateTime.UtcNow >= until) return mode;
            await Task.Delay(_options.ModePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Why an upload was refused, in plain words.</summary>
    public static string BusyMessage(string mode) => mode switch
    {
        "P_WORK" => "A job is already loaded on the machine, waiting for its start button. Start it, or cancel it (Stop on the Job tab, or long-press the machine's button), then send again.",
        "P_WORKING" => "The machine is running a job. Wait for it to finish, or stop it, then send again.",
        "P_PAUSE" => "A job is paused on the machine. Continue or stop it, then send again.",
        "P_SLEEP" => "The machine is asleep. Press its button to wake it, then send again.",
        "P_AUTOFOCUS" or "P_MEASURE" => "The machine is measuring or focusing. Wait for it to finish, then send again.",
        "P_FRAMING" => "The machine is tracing the outline (framing). Wait for it to finish or stop it, then send again.",
        "P_ERROR" => "The machine is reporting an error. Check it (the light or the Machine tab), then send again.",
        _ => $"The machine isn't ready for a new job (it reports \"{mode}\"). Wait a moment and try again.",
    };

    /// <summary>Camera still as JPEG bytes. Stream 0 = overview ("main"), 1 = close-up ("deep").</summary>
    public async Task<byte[]> SnapshotAsync(int stream = 0, CancellationToken cancellationToken = default)
    {
        if (stream is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(stream), stream, "Stream must be 0 (overview) or 1 (close-up).");
        const string url = "/v1/camera/snap";
        // These must go in params, not data ("name is not string" otherwise).
        var parameters = new JsonObject { ["name"] = stream == 1 ? "deep" : "main", ["stream"] = stream.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var res = AsObject(await GetAsync(url, parameters: parameters, timeout: _options.SnapshotTimeout, cancellationToken: cancellationToken).ConfigureAwait(false), url);
        string filename = Json.Str(res, "filename")
            ?? throw new DeviceException(DeviceException.ProtocolErrorCode, $"{url}: the reply had no filename.", res);
        return await DownloadFileAsync(filename, ImageFileType, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ---- misc ---------------------------------------------------------------------------------

    void Log(string message)
    {
        try
        {
            _options.Log?.Invoke(message);
        }
        catch (Exception)
        {
            // Logging must never break the link.
        }
    }

    static string Seconds(TimeSpan t) => $"{t.TotalSeconds:0.#} s";

    sealed class UploadState(byte[] data, IProgress<double>? progress)
    {
        long _sent;
        public byte[] Data { get; } = data;
        public volatile int PacketSize = DeviceClient.PacketSize;
        public TaskCompletionSource<JsonObject> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Released on every sign of life from the device.</summary>
        public SemaphoreSlim Activity { get; } = new(0);
        public long Sent => Interlocked.Read(ref _sent);

        public void Touch() => Activity.Release();

        public void ReportSent(long end)
        {
            long sent = InterlockedMax(ref _sent, end);
            progress?.Report(Data.Length == 0 ? 1.0 : (double)sent / Data.Length);
        }

        public void Finish(JsonObject result)
        {
            Done.TrySetResult(result);
            Activity.Release();
        }

        public void Fail(Exception error)
        {
            Done.TrySetException(error);
            Activity.Release();
        }

        static long InterlockedMax(ref long target, long value)
        {
            long current = Interlocked.Read(ref target);
            while (value > current)
            {
                long seen = Interlocked.CompareExchange(ref target, value, current);
                if (seen == current)
                    return value;
                current = seen;
            }
            return current;
        }
    }

    sealed class DownloadState(int size)
    {
        readonly Lock _gate = new();
        long _windowReceived;
        Exception? _failure;
        public byte[] Buffer { get; } = new byte[size];
        public SemaphoreSlim Signal { get; } = new(0);
        public long WindowReceived { get { lock (_gate) return _windowReceived; } }

        public void BeginWindow()
        {
            lock (_gate)
                _windowReceived = 0;
        }

        public void Write(long offset, ReadOnlySpan<byte> chunk)
        {
            lock (_gate)
            {
                if (offset < 0 || offset + chunk.Length > Buffer.Length)
                {
                    _failure ??= new DeviceException(DeviceException.ProtocolErrorCode,
                        $"The machine sent file data outside the file (offset {offset}, {chunk.Length} bytes, size {Buffer.Length}).");
                }
                else
                {
                    chunk.CopyTo(Buffer.AsSpan((int)offset));
                    _windowReceived += chunk.Length;
                }
            }
            Signal.Release();
        }

        public void Fail(Exception error)
        {
            lock (_gate)
                _failure ??= error;
            Signal.Release();
        }

        public void ThrowIfFailed()
        {
            Exception? f;
            lock (_gate)
                f = _failure;
            if (f is not null)
                throw f is DeviceException de ? new DeviceException(de.Code, de.Message, de.ResponseData, de) : f;
        }
    }
}

/// <summary>One framed WebSocket (instruction, file_stream or media_stream). Sends are serialized.</summary>
internal sealed class FramedSocket(
    WebSocket ws,
    string function,
    FrameDecoder decoder,
    Func<FramedSocket, DecodedFrame, ValueTask> onFrame,
    Action<FramedSocket, Exception> onClosed,
    Action onInbound,
    DeviceClientOptions options)
{
    readonly SemaphoreSlim _sendLock = new(1, 1);
    Task? _reader;

    public string Function { get; } = function;

    public void Start(CancellationToken lifetime) => _reader = Task.Run(() => ReadLoopAsync(lifetime), CancellationToken.None);

    public Task SendAsync(byte[] frame, CancellationToken lifetime) => SendAsync(frame, lifetime, lifetime);

    /// <summary>Sends one frame. <paramref name="waitToken"/> only cancels waiting for the send lock, never a half-sent frame.</summary>
    public async Task SendAsync(byte[] frame, CancellationToken lifetime, CancellationToken waitToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, waitToken);
        await _sendLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            Trace("->", frame.AsSpan(Frame.HeaderLength), (ProtocolType)(frame[5] & 0x7F));
            await ws.SendAsync(frame, WebSocketMessageType.Binary, endOfMessage: true, lifetime).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    async Task ReadLoopAsync(CancellationToken lifetime)
    {
        var buffer = new byte[64 * 1024];
        Exception reason;
        try
        {
            while (true)
            {
                var r = await ws.ReceiveAsync(buffer.AsMemory(), lifetime).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    reason = new DeviceException(DeviceException.DisconnectedCode, $"The machine closed the {Function} channel.");
                    break;
                }
                onInbound();
                foreach (var frame in decoder.Feed(buffer.AsSpan(0, r.Count)))
                {
                    Trace("<-", frame.Payload, frame.Type);
                    try
                    {
                        await onFrame(this, frame).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        options.Log?.Invoke($"[{Function}] frame handler failed: {e}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            if (lifetime.IsCancellationRequested)
                return;
            reason = e;
        }
        onClosed(this, reason);
    }

    void Trace(string dir, ReadOnlySpan<byte> payload, ProtocolType type)
    {
        if (!options.TraceFrames || options.Log is not { } log)
            return;
        string preview = type == ProtocolType.Json
            ? Encoding.UTF8.GetString(payload[..Math.Min(200, payload.Length)])
            : Convert.ToHexString(payload[..Math.Min(16, payload.Length)]);
        log($"  {dir} [{Function}] type={(int)type} {payload.Length}B {preview}");
    }

    public void Abort()
    {
        try
        {
            ws.Abort();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async Task CloseAsync()
    {
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Best effort.
        }
        Abort();
        if (_reader is { } r)
            await Task.WhenAny(r, Task.Delay(2000)).ConfigureAwait(false);
        ws.Dispose();
    }
}

/// <summary>What the machine is doing (from <c>/v1/device/runtime-infos</c>).</summary>
/// <param name="Mode">Normalised to the event words: P_IDLE, P_WORK (loaded, waiting for the button), P_WORKING, P_PAUSE…</param>
/// <param name="RawMode">As reported, e.g. <c>Work</c>.</param>
/// <param name="TaskId">The loaded or running job's task id, if any.</param>
public sealed record RuntimeState(string Mode, string RawMode, string? SubMode, string? Desc, string? TaskId);
