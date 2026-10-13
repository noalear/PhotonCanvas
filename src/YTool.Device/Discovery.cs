using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocketProtocol = System.Net.Sockets.ProtocolType;

namespace YTool.Device;

/// <summary>A device that answered discovery. <see cref="Raw"/> holds every field it reported (merged across replies).</summary>
public sealed record DiscoveredDevice(
    string Ip,
    string? Name,
    string? DeviceCode,
    string? Sn,
    string? Firmware,
    string Protocol,
    JsonObject Raw)
{
    /// <summary>Source address of the (last) reply datagram; normally equal to <see cref="Ip"/>.</summary>
    public string? From { get; init; }

    public bool IsV2 => Protocol == Discovery.ProtocolV2;
}

/// <summary>
/// LAN discovery for xTool devices: V2 AES-256-CBC encrypted UDP multicast and V1 plain-JSON
/// multicast. See docs/protocol-f2-ultra.md, "Discovery".
/// </summary>
public static class Discovery
{
    public const string ProtocolV1 = "V1";
    public const string ProtocolV2 = "V2";

    /// <summary>Requests are encrypted with this key.</summary>
    public static ReadOnlySpan<byte> PrimaryKey => "<discovery-request-key>"u8;

    /// <summary>Replies are decrypted with this key; it is also sent inside the request.</summary>
    public static ReadOnlySpan<byte> CommonKey => "<discovery-reply-key>"u8;

    static readonly (string Group, int Port, int Ttl)[] V2Groups =
    [
        ("224.0.0.251", 5353, 1),
        ("224.0.0.252", 5354, 1),
        ("239.0.1.251", 25353, 4),
        ("239.0.1.252", 25354, 4),
    ];

    static readonly IPEndPoint V1Group = new(IPAddress.Parse("224.0.1.77"), 20000);

    static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(1.5);

    // ---- crypto ---------------------------------------------------------------------------

    /// <summary>AES-256-CBC, PKCS7, random IV. Wire format: 16-byte IV followed by the ciphertext.</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        Span<byte> iv = stackalloc byte[16];
        RandomNumberGenerator.Fill(iv);
        using var aes = Aes.Create();
        aes.Key = NormalizeKey(key);
        byte[] cipher = aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);
        var blob = new byte[16 + cipher.Length];
        iv.CopyTo(blob);
        cipher.CopyTo(blob, 16);
        return blob;
    }

    /// <summary>Inverse of <see cref="Encrypt"/>. Throws <see cref="CryptographicException"/> on malformed input.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> key)
    {
        if (blob.Length < 32 || (blob.Length - 16) % 16 != 0)
            throw new CryptographicException("Encrypted discovery datagram has an invalid length.");
        using var aes = Aes.Create();
        aes.Key = NormalizeKey(key);
        return aes.DecryptCbc(blob[16..], blob[..16], PaddingMode.PKCS7);
    }

    public static bool TryDecrypt(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> key, out byte[] plaintext)
    {
        try
        {
            plaintext = Decrypt(blob, key);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = [];
            return false;
        }
    }

    // Key is the first 32 bytes, zero-padded.
    static byte[] NormalizeKey(ReadOnlySpan<byte> key)
    {
        var k = new byte[32];
        key[..Math.Min(32, key.Length)].CopyTo(k);
        return k;
    }

    // ---- request / reply ------------------------------------------------------------------

    /// <summary>The V2 request JSON (before encryption).</summary>
    public static byte[] BuildV2Request(uint requestId)
    {
        var msg = new JsonObject
        {
            ["type"] = "deviceFind",
            ["method"] = "request",
            ["data"] = new JsonObject
            {
                ["version"] = "1.0",
                ["clientType"] = "atomnClient",
                ["requestId"] = requestId,
                ["key"] = Encoding.ASCII.GetString(CommonKey),
            },
        };
        return Encoding.UTF8.GetBytes(msg.ToJsonString());
    }

    /// <summary>The V1 (plain JSON) request telling the device where to answer.</summary>
    public static byte[] BuildV1Request(IPAddress localIp, int localPort, long requestId)
    {
        var msg = new JsonObject
        {
            ["ip"] = localIp.ToString(),
            ["port"] = localPort,
            ["requestId"] = requestId,
        };
        return Encoding.UTF8.GetBytes(msg.ToJsonString());
    }

    /// <summary>
    /// Parses one reply datagram. V2: AES-decrypted with <see cref="CommonKey"/>, must be
    /// <c>{"type":"deviceFind","method":"response","data":{...}}</c>. V1: plain JSON with
    /// <c>requestId</c> and <c>sncode</c> or <c>name</c>. Returns null for anything else,
    /// including our own (or another client's) requests.
    /// </summary>
    public static DiscoveredDevice? ParseReply(ReadOnlySpan<byte> datagram, IPAddress? from = null)
    {
        JsonObject? raw = ParseReplyObject(datagram);
        if (raw is null)
            return null;
        string fromText = from?.ToString() ?? "";
        return FromRaw(raw, fromText);
    }

    static JsonObject? ParseReplyObject(ReadOnlySpan<byte> datagram)
    {
        if (TryDecrypt(datagram, CommonKey, out byte[] plain))
        {
            JsonObject? msg = Json.TryParseObject(plain);
            if (msg is not null)
            {
                if (Json.Str(msg, "type") == "deviceFind" && Json.Str(msg, "method") == "response")
                {
                    var result = new JsonObject { ["protocol"] = ProtocolV2 };
                    if (msg["data"] is JsonObject data)
                        foreach (var (k, v) in data)
                            result[k] = v?.DeepClone();
                    return result;
                }
                return null; // a request (ours echoed back, or another client's)
            }
        }
        JsonObject? v1 = Json.TryParseObject(datagram);
        if (v1 is not null && (v1.ContainsKey("sncode") || v1.ContainsKey("name")) && v1.ContainsKey("requestId"))
        {
            var result = new JsonObject { ["protocol"] = ProtocolV1 };
            foreach (var (k, v) in v1)
                result[k] = v?.DeepClone();
            return result;
        }
        return null;
    }

    static DiscoveredDevice FromRaw(JsonObject raw, string from)
    {
        string protocol = Json.Str(raw, "protocol") ?? ProtocolV1;
        string ip = Json.Str(raw, "deviceIp") ?? Json.Str(raw, "ip") ?? from;
        return new DiscoveredDevice(
            Ip: ip,
            Name: Json.Str(raw, "deviceName") ?? Json.Str(raw, "name"),
            DeviceCode: Json.Str(raw, "deviceCode"),
            Sn: Json.Str(raw, "deviceSn") ?? Json.Str(raw, "sncode"),
            Firmware: Json.Str(raw, "firmwareVersion") ?? Json.Str(raw, "version"),
            Protocol: protocol,
            Raw: raw)
        { From = from.Length == 0 ? null : from };
    }

    // ---- interfaces -----------------------------------------------------------------------

    /// <summary>IPv4 addresses of every interface that is up, excluding loopback.</summary>
    public static IReadOnlyList<IPAddress> LocalIPv4Addresses()
    {
        var result = new List<IPAddress>();
        NetworkInterface[] nics;
        try
        {
            nics = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return result;
        }
        foreach (var nic in nics)
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    var a = ua.Address;
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !result.Contains(a))
                        result.Add(a);
                }
            }
            catch (NetworkInformationException)
            {
                // Skip interfaces we can't query.
            }
        }
        return result;
    }

    // ---- discovery ------------------------------------------------------------------------

    /// <summary>
    /// Sends discovery requests on every local IPv4 interface (or unicast to <paramref name="targetIp"/>,
    /// e.g. for USB links) every 1.5 s until <paramref name="timeout"/> (default 4 s) and returns every
    /// device that answered, keyed by IP. Interfaces or ports that can't be bound are skipped.
    /// Throws <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/> fires.
    /// </summary>
    public static async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        TimeSpan? timeout = null,
        IPAddress? targetIp = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var duration = timeout ?? TimeSpan.FromSeconds(4);
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

        var ifaces = LocalIPv4Addresses();
        log?.Invoke($"[discover] local interfaces: {string.Join(", ", ifaces)}");
        var ownAddresses = new HashSet<IPAddress>(ifaces);

        var senders = new List<(Socket Socket, IPAddress Ip)>();
        var receivers = new List<Socket>();
        var found = new Dictionary<string, (JsonObject Raw, string From)>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(duration);
        try
        {
            foreach (var ip in ifaces)
            {
                var s = TryCreateSender(ip, log);
                if (s is not null)
                    senders.Add((s, ip));
            }
            if (senders.Count == 0)
            {
                // No usable interface: fall back to the OS default route.
                var s = TryCreateSender(IPAddress.Any, log);
                if (s is not null)
                    senders.Add((s, IPAddress.Any));
            }
            foreach (var (group, port, _) in V2Groups)
            {
                var r = TryCreateGroupReceiver(IPAddress.Parse(group), port, ifaces, log);
                if (r is not null)
                    receivers.Add(r);
            }

            var loops = new List<Task>();
            foreach (var s in senders.Select(x => x.Socket).Concat(receivers))
                loops.Add(ReceiveLoop(s, ownAddresses, found, log, cts.Token));
            loops.Add(SendLoop(senders, targetIp, log, cts.Token));
            await Task.WhenAll(loops).ConfigureAwait(false);
        }
        finally
        {
            foreach (var (s, _) in senders)
                s.Dispose();
            foreach (var s in receivers)
                s.Dispose();
        }
        cancellationToken.ThrowIfCancellationRequested();

        lock (found)
        {
            return [.. found.Values.Select(v => FromRaw(v.Raw, v.From)).OrderBy(d => d.Ip, StringComparer.Ordinal)];
        }
    }

    static Socket? TryCreateSender(IPAddress ip, Action<string>? log)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, SocketProtocol.Udp);
        try
        {
            s.Bind(new IPEndPoint(ip, 0));
            if (!ip.Equals(IPAddress.Any))
                s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ip.GetAddressBytes());
            return s;
        }
        catch (SocketException e)
        {
            log?.Invoke($"[discover] can't send via {ip}: {e.Message}");
            s.Dispose();
            return null;
        }
    }

    static Socket? TryCreateGroupReceiver(IPAddress group, int port, IReadOnlyList<IPAddress> ifaces, Action<string>? log)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, SocketProtocol.Udp);
        try
        {
            s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            s.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (SocketException e)
        {
            log?.Invoke($"[discover] can't listen on port {port}: {e.Message}");
            s.Dispose();
            return null;
        }
        int joined = 0;
        foreach (var ip in ifaces)
        {
            try
            {
                s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(group, ip));
                joined++;
            }
            catch (SocketException e)
            {
                log?.Invoke($"[discover] can't join {group}:{port} via {ip}: {e.Message}");
            }
        }
        if (joined == 0)
        {
            s.Dispose();
            return null;
        }
        return s;
    }

    static async Task SendLoop(List<(Socket Socket, IPAddress Ip)> senders, IPAddress? targetIp, Action<string>? log, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                SendAll(senders, targetIp, log);
                await Task.Delay(ResendInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Deadline reached.
        }
    }

    static void SendAll(List<(Socket Socket, IPAddress Ip)> senders, IPAddress? targetIp, Action<string>? log)
    {
        byte[] v2 = BuildV2Request(BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)));
        foreach (var (s, ip) in senders)
        {
            try
            {
                int localPort = ((IPEndPoint)s.LocalEndPoint!).Port;
                byte[] v1 = BuildV1Request(ip, localPort, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (targetIp is not null)
                {
                    foreach (var (_, port, _) in V2Groups)
                        s.SendTo(Encrypt(v2, PrimaryKey), new IPEndPoint(targetIp, port));
                    s.SendTo(v1, new IPEndPoint(targetIp, V1Group.Port));
                }
                else
                {
                    foreach (var (group, port, ttl) in V2Groups)
                    {
                        s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, ttl);
                        s.SendTo(Encrypt(v2, PrimaryKey), new IPEndPoint(IPAddress.Parse(group), port));
                    }
                    s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
                    s.SendTo(v1, V1Group);
                }
            }
            catch (SocketException e)
            {
                log?.Invoke($"[discover] send via {ip} failed: {e.Message}");
            }
        }
    }

    static async Task ReceiveLoop(
        Socket s,
        HashSet<IPAddress> ownAddresses,
        Dictionary<string, (JsonObject Raw, string From)> found,
        Action<string>? log,
        CancellationToken ct)
    {
        var buffer = new byte[65535];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        int errors = 0;
        while (!ct.IsCancellationRequested)
        {
            SocketReceiveFromResult r;
            try
            {
                r = await s.ReceiveFromAsync(buffer, SocketFlags.None, any, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException e)
            {
                // Windows reports ICMP "port unreachable" from earlier sends as ConnectionReset.
                if (e.SocketErrorCode != SocketError.ConnectionReset && ++errors > 10)
                {
                    log?.Invoke($"[discover] receive on {s.LocalEndPoint} stopped: {e.Message}");
                    return;
                }
                continue;
            }
            if (r.RemoteEndPoint is not IPEndPoint from || ownAddresses.Contains(from.Address))
                continue; // our own request echoed back
            JsonObject? raw = ParseReplyObject(buffer.AsSpan(0, r.ReceivedBytes));
            if (raw is null)
            {
                log?.Invoke($"[discover] unparsed {r.ReceivedBytes} B from {from}");
                continue;
            }
            string fromText = from.Address.ToString();
            string ip = Json.Str(raw, "deviceIp") ?? Json.Str(raw, "ip") ?? fromText;
            lock (found)
            {
                if (found.TryGetValue(ip, out var existing))
                {
                    bool keepV2 = Json.Str(existing.Raw, "protocol") == ProtocolV2 && Json.Str(raw, "protocol") != ProtocolV2;
                    foreach (var (k, v) in raw)
                    {
                        if (keepV2 && k == "protocol")
                            continue;
                        existing.Raw[k] = v?.DeepClone();
                    }
                    found[ip] = (existing.Raw, fromText);
                }
                else
                {
                    found[ip] = (raw, fromText);
                }
            }
        }
    }
}
