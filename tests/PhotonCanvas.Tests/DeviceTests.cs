using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PhotonCanvas.Device;
using ProtocolType = PhotonCanvas.Device.ProtocolType;

namespace PhotonCanvas.Tests;

// Nothing here touches the network except loopback (127.0.0.1) sockets to an in-process fake device.

public class DeviceFrameTests
{
    static byte[] Hex(string s) => Convert.FromHexString(s);

    [Fact]
    public void Crc16KnownVectors()
    {
        Assert.Equal(0xBB3D, Crc16.Compute("123456789"u8));
        Assert.Equal(0x0000, Crc16.Compute([]));
        // Values below come from the verified Python implementation (src/pcprobe/frame.py).
        Assert.Equal(0x30C0, Crc16.Compute("A"u8));
        Assert.Equal(0xBAD3, Crc16.Compute([.. Enumerable.Range(0, 256).Select(i => (byte)i)]));
    }

    [Fact]
    public void EncodeMatchesPythonReference()
    {
        Assert.Equal(Hex("babe00000704898f81b37b2261223a317d"), Frame.Encode("{\"a\":1}"));
        // File request packet, sent with no body CRC (type 33 | 0x80).
        byte[] req = [1, 1, 0, 0, 0, 0, 0, 0, 0x10, 0];
        Assert.Equal(Hex("babe00000aa100003a8701010000000000001000"), Frame.Encode(req, ProtocolType.FileTransfer, bodyCrc: false));
    }

    [Fact]
    public void RoundTripSeveralFramesInOneChunk()
    {
        var a = Frame.Encode("{\"x\":1}");
        var b = Frame.Encode([1, 2, 3], ProtocolType.FileTransfer, bodyCrc: false);
        var c = Frame.Encode([], ProtocolType.Buffer);
        var dec = new FrameDecoder();
        var frames = dec.Feed([.. a, .. b, .. c]);
        Assert.Equal(3, frames.Count);
        Assert.Equal(ProtocolType.Json, frames[0].Type);
        Assert.Equal("{\"x\":1}", Encoding.UTF8.GetString(frames[0].Payload));
        Assert.Equal(ProtocolType.FileTransfer, frames[1].Type); // bit 7 stripped
        Assert.Equal([1, 2, 3], frames[1].Payload);
        Assert.Equal(ProtocolType.Buffer, frames[2].Type);
        Assert.Empty(frames[2].Payload);
        Assert.Equal(0, dec.BufferedBytes);
    }

    [Fact]
    public void SplitFramesByteByByte()
    {
        var payload = new byte[5000];
        new Random(1).NextBytes(payload);
        byte[] stream = [.. Frame.Encode(payload, ProtocolType.Buffer), .. Frame.Encode("{}")];
        var dec = new FrameDecoder();
        var got = new List<DecodedFrame>();
        foreach (byte x in stream)
            got.AddRange(dec.Feed([x]));
        Assert.Equal(2, got.Count);
        Assert.Equal(payload, got[0].Payload);
        Assert.Equal("{}", Encoding.UTF8.GetString(got[1].Payload));
    }

    [Fact]
    public void ResyncsAfterGarbageAndBadCrc()
    {
        var good1 = Frame.Encode("{\"n\":1}");
        var good2 = Frame.Encode("{\"n\":2}");
        var badBody = Frame.Encode("{\"n\":3}");
        badBody[^1] ^= 0xFF; // corrupt payload: body CRC no longer matches
        var badHeader = Frame.Encode("{\"n\":4}");
        badHeader[9] ^= 0xFF; // corrupt header CRC
        byte[] garbage = [0x00, 0xBA, 0x11, 0xBE, 0xBA];
        var dec = new FrameDecoder();
        var frames = dec.Feed([.. garbage, .. good1, .. badBody, .. garbage, .. badHeader, .. good2]);
        Assert.Equal(["{\"n\":1}", "{\"n\":2}"], frames.Select(f => Encoding.UTF8.GetString(f.Payload)));
    }

    [Fact]
    public void BogusHeaderWithHugeLengthDoesNotStall()
    {
        // Magic followed by a huge length but a wrong header CRC must not make the decoder wait for 16 MB.
        byte[] bogus = [0xBA, 0xBE, 0xFF, 0xFF, 0xFF, 0x04, 0, 0, 0, 0];
        var dec = new FrameDecoder();
        var frames = dec.Feed([.. bogus, .. Frame.Encode("{}")]);
        Assert.Single(frames);
    }

    [Fact]
    public void SkipBodyCrcForConfiguredTypes()
    {
        var media = Frame.Encode([9, 9, 9], ProtocolType.MediaStream);
        media[^1] = 7; // payload no longer matches the body CRC (flag bit not set); header still valid
        Assert.Empty(new FrameDecoder().Feed(media));
        var frames = new FrameDecoder([ProtocolType.MediaStream]).Feed(media);
        Assert.Equal([9, 9, 7], Assert.Single(frames).Payload);
    }

    [Fact]
    public void RejectsOversizedPayload()
    {
        Assert.Throws<ArgumentException>(() => Frame.Encode(new byte[Frame.MaxPayloadLength + 1], ProtocolType.Buffer));
    }
}

public class DeviceDiscoveryTests
{
    // Stand-in keys: tests never contain or need the real machine keys.
    const string TestRequestKey = "test-request-key-0123456789abcde";
    const string TestReplyKey = "test-reply-key-0123456789abcdefg";
    static byte[] Req => Encoding.ASCII.GetBytes(TestRequestKey);
    static byte[] Rep => Encoding.ASCII.GetBytes(TestReplyKey);

    [Fact]
    public void AesRoundTrip()
    {
        var plain = "{\"hello\":\"world\"}"u8.ToArray();
        var blob = Discovery.Encrypt(plain, Req);
        Assert.Equal(16 + 32, blob.Length); // IV + 17 bytes padded to 32
        Assert.Equal(plain, Discovery.Decrypt(blob, Req));
        Assert.NotEqual(blob, Discovery.Encrypt(plain, Req)); // random IV
        Assert.False(Discovery.TryDecrypt([1, 2, 3], Rep, out _));
    }

    [Fact]
    public void ParsesV2Reply()
    {
        var small = Discovery.Encrypt("""{"type":"deviceFind","method":"response","data":{"deviceName":"F2 Ultra"}}"""u8.ToArray(), Rep);
        var d = Discovery.ParseReply(small, TestReplyKey, IPAddress.Parse("10.0.0.7"));
        Assert.NotNull(d);
        Assert.Equal(("10.0.0.7", "F2 Ultra", "V2"), (d.Ip, d.Name, d.Protocol));
        Assert.True(d.IsV2);
        Assert.Null(Discovery.ParseReply(small, TestRequestKey)); // wrong key: unreadable

        var full = new JsonObject
        {
            ["type"] = "deviceFind",
            ["method"] = "response",
            ["data"] = new JsonObject
            {
                ["deviceIp"] = "192.0.2.5",
                ["deviceName"] = "xTool F2 Ultra",
                ["deviceCode"] = "GS004-CLASS-4",
                ["deviceSn"] = "SN123",
                ["firmwareVersion"] = "40.54.021",
                ["platformVersion"] = "1.5",
            },
        };
        var blob = Discovery.Encrypt(Encoding.UTF8.GetBytes(full.ToJsonString()), Rep);
        d = Discovery.ParseReply(blob, TestReplyKey, IPAddress.Parse("192.0.2.99"));
        Assert.NotNull(d);
        Assert.Equal("192.0.2.5", d.Ip); // deviceIp wins over the datagram source
        Assert.Equal("192.0.2.99", d.From);
        Assert.Equal(("GS004-CLASS-4", "SN123", "40.54.021"), (d.DeviceCode, d.Sn, d.Firmware));
        Assert.Equal("1.5", d.Raw["platformVersion"]!.GetValue<string>());
    }

    [Fact]
    public void IgnoresRequestsIncludingOurOwn()
    {
        // Our own request echoed back: encrypted with the primary key, so the common key can't read it.
        var ours = Discovery.Encrypt(Discovery.BuildV2Request(42, TestReplyKey), Req);
        Assert.Null(Discovery.ParseReply(ours, TestReplyKey));
        // Another client's request readable with the common key: still a request, not a device.
        var other = Discovery.Encrypt(Discovery.BuildV2Request(43, TestReplyKey), Rep);
        Assert.Null(Discovery.ParseReply(other, TestReplyKey));
        // Our V1 request {ip, port, requestId}.
        Assert.Null(Discovery.ParseReply(Discovery.BuildV1Request(IPAddress.Parse("192.0.2.1"), 5000, 1), TestReplyKey));
        Assert.Null(Discovery.ParseReply("not json"u8, TestReplyKey));
        Assert.Null(Discovery.ParseReply([], TestReplyKey));
    }

    [Fact]
    public void V2RequestContent()
    {
        var plain = Discovery.Decrypt(Discovery.Encrypt(Discovery.BuildV2Request(7, TestReplyKey), Req), Req);
        var msg = JsonNode.Parse(plain)!.AsObject();
        Assert.Equal("deviceFind", msg["type"]!.GetValue<string>());
        Assert.Equal("request", msg["method"]!.GetValue<string>());
        Assert.Equal("atomnClient", msg["data"]!["clientType"]!.GetValue<string>());
        Assert.Equal(7u, msg["data"]!["requestId"]!.GetValue<uint>());
        Assert.Equal(TestReplyKey, msg["data"]!["key"]!.GetValue<string>());
    }

    [Fact]
    public void ParsesV1Reply()
    {
        var d = Discovery.ParseReply("""{"ip":"192.0.2.8","name":"F1","version":"V40.31","sncode":"MXF1","requestId":1700000000000}"""u8, TestReplyKey, IPAddress.Parse("192.0.2.8"));
        Assert.NotNull(d);
        Assert.Equal(("192.0.2.8", "F1", "MXF1", "V40.31", "V1"), (d.Ip, d.Name, d.Sn, d.Firmware, d.Protocol));
        Assert.Null(d.DeviceCode);

        // No ip in the reply: fall back to the datagram's source.
        d = Discovery.ParseReply("""{"name":"F1","requestId":1}"""u8, TestReplyKey, IPAddress.Parse("192.0.2.9"));
        Assert.Equal("192.0.2.9", d?.Ip);
    }
}

public class DeviceMessageTests
{
    static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void ParsesNestedEvent()
    {
        var ev = DeviceEvent.Parse(Obj("""{"type":"request","method":"REPORT","url":"/v1/device/event","data":{"module":"WORK_RESULT","type":"WORK_FINISHED","info":{"result":"failed","timeUse":10},"level":1}}"""));
        Assert.Equal(("WORK_RESULT", "WORK_FINISHED", "1", "/v1/device/event", "REPORT"), (ev.Module, ev.Type, ev.Level, ev.Url, ev.Method));
        Assert.Equal("failed", ev.Info!["result"]!.GetValue<string>());

        var button = DeviceEvent.TryParse("""{"type":"request","method":"NOTIFY","url":"/x","data":{"module":"BUTTON","type":"SHOERT_PRESS","info":"1","level":"info"}}"""u8);
        Assert.NotNull(button);
        Assert.Equal(("BUTTON", "SHOERT_PRESS", "1", "info"), (button.Module, button.Type, button.InfoText, button.Level));
        Assert.Null(DeviceEvent.TryParse("[1,2]"u8));
    }

    [Fact]
    public void ParsesFlatEventAndEnvelopeWithoutEvent()
    {
        var flat = DeviceEvent.Parse(Obj("""{"module":"MODE_CHANGE","type":"P_IDLE","info":2,"level":0}"""));
        Assert.Equal(("MODE_CHANGE", "P_IDLE", "2"), (flat.Module, flat.Type, flat.InfoText));

        var bare = DeviceEvent.Parse(Obj("""{"type":"request","method":"REPORT","url":"/v1/x","data":{"foo":1}}"""));
        Assert.Null(bare.Type); // "request" is the envelope type, not an event type
        Assert.Null(bare.Module);
        Assert.Equal("/v1/x", bare.Url);
    }

    [Fact]
    public void ParsesResponses()
    {
        // The device pretty-prints with tabs.
        var r = DeviceResponse.TryParse(Obj("{\n\t\"type\":\t\"response\",\n\t\"transactionId\":\t3,\n\t\"code\":\t0,\n\t\"message\":\t\"user auth success\",\n\t\"data\":\t{}\n}"));
        Assert.NotNull(r);
        Assert.True(r.IsSuccess);
        Assert.Equal((3, "user auth success"), (r.TransactionId, r.Message));

        r = DeviceResponse.TryParse(Obj("""{"type":"response","transactionId":4,"code":-1,"message":"name is not string"}"""));
        Assert.Equal(-1, r?.Code);
        Assert.Null(DeviceResponse.TryParse(Obj("""{"type":"request","transactionId":4}""")));
        Assert.Equal(DeviceException.ProtocolErrorCode, DeviceResponse.TryParse(Obj("""{"type":"response","transactionId":5}"""))?.Code);
    }

    [Fact]
    public void BuildsRequests()
    {
        var json = DeviceClient.BuildRequest("GET", "/v1/camera/snap", null, new JsonObject { ["name"] = "main" }, 12, null, 1000);
        var msg = Obj(json);
        Assert.Equal(["type", "method", "url", "params", "data", "timestamp", "transactionId"], msg.Select(kv => kv.Key));
        Assert.Equal("main", msg["params"]!["name"]!.GetValue<string>());
        Assert.Empty(msg["data"]!.AsObject());
        Assert.Equal(12, msg["transactionId"]!.GetValue<int>());
        Assert.Equal("tok", Obj(DeviceClient.BuildRequest("GET", "/u", null, null, 1, "tok", 0))["sessionToken"]!.GetValue<string>());
    }

    [Fact]
    public void ReadableErrors()
    {
        var e = DeviceException.FromResponse(-1, "name is not string", "/v1/camera/snap");
        Assert.Equal(-1, e.Code);
        Assert.Contains("/v1/camera/snap", e.Message);
        Assert.Contains("name is not string", e.Message);
        Assert.Contains("expired", DeviceException.FromResponse(403, null, "/v1/x").Message);
        Assert.True(DeviceException.FromResponse(403, null, "/v1/x").IsConnectionProblem);
    }

    [Fact]
    public void ParsesMachineInfo()
    {
        var nested = MachineInfo.Parse(Obj("""{"sn":"S2","hardwareVersion":"HW9","firmware":{"package_version":"40.54.021.00.ht6","version":{"display_app":"40.54.003"}}}"""));
        Assert.Equal("40.54.021.00.ht6", nested.Firmware); // as reported by the user's F2 Ultra
        var info = MachineInfo.Parse(Obj("""{"sn":"S1","laserType":["RED","BLUE"],"laserPower":[60,40],"gcodeFormat":["txt","bin"],"packageFormat":["xf"]}"""));
        Assert.Equal(["RED", "BLUE"], info.LaserType);
        Assert.Equal([60.0, 40.0], info.LaserPower);
        Assert.True(info.AcceptsTextGcode);
        Assert.Equal("S1", info.Sn);
    }

    [Fact]
    public void TokenStoreRoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), "photoncanvas-test-" + Guid.NewGuid().ToString("N"), "state.json");
        try
        {
            Assert.Null(TokenStore.GetToken(path, "192.0.2.1"));
            TokenStore.SaveToken(path, "192.0.2.1", "abc");
            TokenStore.SaveToken(path, "192.0.2.2", "def");
            Assert.Equal("abc", TokenStore.GetToken(path, "192.0.2.1"));
            Assert.Equal("def", TokenStore.GetToken(path, "192.0.2.2"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}

/// <summary>End-to-end tests of <see cref="DeviceClient"/> against an in-process fake device over loopback sockets.</summary>
[Collection(TimingCollection.Name)]
public sealed class DeviceClientTests : IAsyncLifetime
{
    readonly string _stateDir = Path.Combine(Path.GetTempPath(), "photoncanvas-test-" + Guid.NewGuid().ToString("N"));
    readonly FakeDevice _fake = new();

    string StatePath => Path.Combine(_stateDir, "state.json");

    const string TestPairingKey = "test-pairing-key-012";

    DeviceClientOptions Options() => new()
    {
        PairingKey = TestPairingKey,
        StatePath = StatePath,
        TimeZoneName = "UTC",
        WebSocketFactory = _fake.ConnectAsync,
        RequestTimeout = TimeSpan.FromSeconds(5),
        ButtonPollInterval = TimeSpan.FromMilliseconds(20),
        TransferTimeout = TimeSpan.FromSeconds(5),
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fake.DisposeAsync();
        if (Directory.Exists(_stateDir))
            Directory.Delete(_stateDir, recursive: true);
    }

    async Task<DeviceClient> Connected(DeviceClientOptions? options = null)
    {
        var client = new DeviceClient("192.0.2.10", options ?? Options());
        await client.ConnectAsync();
        return client;
    }

    [Fact]
    public async Task PairsAsGuestAndReadsState()
    {
        TokenStore.SaveToken(StatePath, "192.0.2.10", "cached-token");
        await using var client = await Connected();
        Assert.True(client.IsConnected);

        var parity = _fake.Requests.Single(r => Url(r) == "/v1/user/parity");
        Assert.Equal("mk-guest", parity["data"]!["userID"]!.GetValue<string>());
        Assert.Equal(TestPairingKey, parity["data"]!["userKey"]!.GetValue<string>());
        Assert.Equal("cached-token", parity["data"]!["token"]!.GetValue<string>());
        Assert.Equal(1, parity["transactionId"]!.GetValue<int>());
        Assert.Equal("new-token", TokenStore.GetToken(StatePath, "192.0.2.10"));

        Assert.Equal("P_IDLE", await client.GetRuntimeModeAsync());
        Assert.Equal(27.058, await client.GetHeadZAsync());
        Assert.Equal("coord", _fake.Requests.Last(r => Url(r) == "/v1/laser-head/focus/parameter")["data"]!["action"]!.GetValue<string>());
        Assert.True((await client.GetMachineInfoAsync()).AcceptsTextGcode);
        Assert.Equal(2, ((JsonObject)(await client.GetProgressAsync())!)["progress"]!.GetValue<int>());

        // Lid-open processing is gapCheckWithKey inverted.
        Assert.True(await client.GetLidOpenProcessingAsync());
        await client.SetLidOpenProcessingAsync(false);
        var put = _fake.Requests.Last(r => Url(r) == "/v1/device/configs");
        Assert.Equal("PUT", put["method"]!.GetValue<string>());
        Assert.True(put["data"]!["kv"]!["gapCheckWithKey"]!.GetValue<bool>());
        Assert.False(await client.GetLidOpenProcessingAsync());

        // Beeper: beepEnable, not inverted, and it must not touch the lid setting.
        Assert.True(await client.GetBeeperEnabledAsync());
        await client.SetBeeperEnabledAsync(false);
        var beepPut = _fake.Requests.Last(r => Url(r) == "/v1/device/configs");
        Assert.False(beepPut["data"]!["kv"]!["beepEnable"]!.GetValue<bool>());
        Assert.Null(beepPut["data"]!["kv"]!["gapCheckWithKey"]);
        Assert.False(await client.GetBeeperEnabledAsync());
        Assert.False(await client.GetLidOpenProcessingAsync());
    }

    [Fact]
    public async Task AutoFocusWaitsForTheMachineAndReturnsTheNewZ()
    {
        await using var client = await Connected();
        Assert.Equal(9.5, await client.AutoFocusAsync());
        Assert.Equal(["P_AUTOFOCUS"], _fake.Modes);
        var start = _fake.Requests.Single(r => Url(r) == "/v1/laser-head/focus/control");
        Assert.Equal("POST", start["method"]!.GetValue<string>());
        Assert.Equal("auto_start", start["data"]!["action"]!.GetValue<string>());
    }

    [Fact]
    public async Task MovesTheFocusToATypedDistanceLikeXToolStudio()
    {
        await using var client = await Connected();
        Assert.Equal(26, await client.MoveFocusAsync(20)); // head Z = distance + 6
        Assert.Equal(["P_AUTOFOCUS"], _fake.Modes);
        var go = _fake.Requests.Single(r => Url(r) == "/v1/laser-head/focus/control")["data"]!;
        Assert.Equal(("goTo", 26.0, 1), (go["action"]!.GetValue<string>(), go["z"]!.GetValue<double>(), go["stopFirst"]!.GetValue<int>()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.MoveFocusAsync(151));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.MoveFocusAsync(-1));
    }

    [Fact]
    public async Task AutoFocusStoppedOrSilentReturnsToIdleWithAReadableError()
    {
        await using var client = await Connected();
        _fake.FocusOutcome = "FOCUS_STOPED";
        var e = await Assert.ThrowsAsync<DeviceException>(() => client.AutoFocusAsync());
        Assert.Contains("button", e.Message);
        Assert.Equal(["P_AUTOFOCUS", "P_IDLE"], _fake.Modes);

        _fake.FocusOutcome = null;
        var t = await Assert.ThrowsAsync<DeviceException>(() => client.AutoFocusAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Contains("didn't finish", t.Message);
        Assert.Equal("P_IDLE", _fake.Modes.Last());
        Assert.Equal(27.058, _fake.HeadZ); // never moved
    }

    [Fact]
    public async Task JobControlAndFillLights()
    {
        await using var client = await Connected();
        await client.PauseJobAsync();
        await client.ResumeJobAsync();
        await client.StopJobAsync();
        Assert.Equal(["pause", "start", "stop"], _fake.States);

        await client.SetFillLightsAsync(50, 100);
        var cfg = _fake.Requests.Last(r => Url(r) == "/v1/device/configs");
        Assert.Equal(136, cfg["data"]!["kv"]!["fillLightBrightFront"]!.GetValue<int>()); // 50 % is what the user's machine reports as 136
        Assert.Equal(255, cfg["data"]!["kv"]!["fillLightBrightBack"]!.GetValue<int>());
        var light = Assert.Single(_fake.Lights);
        Assert.Equal("fill_light", light["params"]!["type"]!.GetValue<string>());
        Assert.Equal("set_bri", light["data"]!["action"]!.GetValue<string>());
        Assert.Equal(0, DeviceClient.PercentToLight(0));
        Assert.Equal(50, DeviceClient.LightToPercent(136));
    }

    [Fact]
    public void ParsesProgressShapes()
    {
        Assert.Equal(0.25, JobProgress.Parse(JsonNode.Parse("""{"curr_progress":"25","total_progress":"100"}""")!.AsObject()).Fraction);
        Assert.Equal(0.4, JobProgress.Parse(JsonNode.Parse("""{"progress":40,"time":12}""")!.AsObject()).Fraction);
        Assert.Equal(12, JobProgress.Parse(JsonNode.Parse("""{"progress":40,"time":12}""")!.AsObject()).ElapsedS);
        Assert.Null(JobProgress.Parse(null).Fraction);
    }

    [Fact]
    public async Task ButtonPairing()
    {
        _fake.ParityCode = 406;
        _fake.AuthStatusPending = 3;
        int prompts = 0;
        await using var client = new DeviceClient("192.0.2.10", Options());
        await client.ConnectAsync(onButtonPressRequired: () => prompts++);
        Assert.Equal(1, prompts);
        Assert.Equal(4, _fake.Requests.Count(r => Url(r) == "/v1/user/auth-status"));
        Assert.Equal("session-1", client.SessionToken);
        Assert.Equal("button-token", TokenStore.GetToken(StatePath, "192.0.2.10"));
    }

    [Fact]
    public async Task PairingRefusedThrowsAndCloses()
    {
        _fake.ParityCode = 409;
        await using var client = new DeviceClient("192.0.2.10", Options());
        var e = await Assert.ThrowsAsync<DeviceException>(() => client.ConnectAsync());
        Assert.Equal(409, e.Code);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ErrorsAndTimeouts()
    {
        await using var client = await Connected();
        var e = await Assert.ThrowsAsync<DeviceException>(() => client.GetAsync("/v1/nope"));
        Assert.Equal(404, e.Code);
        e = await Assert.ThrowsAsync<DeviceException>(() => client.GetAsync("/v1/test/silent", timeout: TimeSpan.FromMilliseconds(200)));
        Assert.Equal(DeviceException.TimeoutCode, e.Code);
        var raw = await client.SendRequestAsync("GET", "/v1/nope");
        Assert.Equal(404, raw.Code);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task RaisesEvents()
    {
        await using var client = await Connected();
        var got = new TaskCompletionSource<DeviceEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceived += ev =>
        {
            if (ev.Type == "WORK_PREPARED")
                got.TrySetResult(ev);
        };
        await _fake.PushAsync("""{"type":"request","method":"REPORT","url":"/v1/device/report","data":{"module":"MODE_CHANGE","type":"P_WORK","info":"","level":0}}""");
        await _fake.PushAsync("""{"type":"request","method":"REPORT","url":"/v1/device/report","data":{"module":"WORK","type":"WORK_PREPARED","info":"","level":0}}""");
        var ev = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("WORK", ev.Module);
    }

    [Fact]
    public async Task UploadsJobWithoutAutoStart()
    {
        await using var client = await Connected();
        var xf = new byte[150_000];
        new Random(7).NextBytes(xf);
        var progress = new List<double>();
        var reporter = new SyncProgress(progress.Add);

        string taskId = await client.UploadJobAsync(xf, reporter);

        Assert.Equal(xf, _fake.Uploaded);
        var announce = _fake.Requests.Single(r => Url(r) == "/v1/filetransfer/upload")["data"]!;
        Assert.Equal("tmp.xf", announce["filename"]!.GetValue<string>());
        Assert.Equal(1, announce["filetype"]!.GetValue<int>());
        Assert.Equal(Convert.ToHexStringLower(MD5.HashData(xf)), announce["digestdata"]!.GetValue<string>());
        var config = _fake.Requests.Single(r => Url(r) == "/v1/processing/upload/config")["data"]!;
        Assert.Equal(0, config["autoStart"]!.GetValue<int>());
        Assert.Equal("xf", config["fileType"]!.GetValue<string>());
        Assert.Equal(taskId, config["taskId"]!.GetValue<string>());
        Assert.Equal(1.0, progress[^1]);
        Assert.True(progress.Count > 2);
        // Every packet respected the device's packet size.
        Assert.All(_fake.UploadPacketSizes, n => Assert.True(n <= FakeDevice.DevicePacketSize));
        // File and instruction sockets share one connection id.
        Assert.Equal([client.ConnectionId], _fake.ConnectionIds.Distinct());
        Assert.Contains("file_stream", _fake.Functions);
    }

    [Fact]
    public async Task RefusesUploadWhenBusy()
    {
        _fake.Mode = "P_WORKING";
        await using var client = await Connected();
        var e = await Assert.ThrowsAsync<DeviceException>(() => client.UploadJobAsync([1, 2, 3]));
        Assert.Equal(DeviceException.BusyCode, e.Code);
        Assert.Equal(DeviceClient.BusyMessage("P_WORKING"), e.Message);
        Assert.DoesNotContain(_fake.Requests, r => Url(r) == "/v1/filetransfer/upload");
        await Assert.ThrowsAsync<ArgumentException>(() => client.UploadFileAsync([1], "fw.bin", fileType: 2));
    }

    [Fact]
    public async Task PutsAFinishedJobAwayBeforeTheNext()
    {
        // After a job the machine stays "finished" until its button is pressed.
        _fake.Mode = "P_WORK_DONE";
        _fake.OnModeSwitch = m => _fake.Mode = m;
        await using var client = await Connected();
        await client.UploadJobAsync([1, 2, 3]);
        Assert.Equal(["P_IDLE"], _fake.Modes);
        Assert.Empty(_fake.States); // nothing stopped or started
        Assert.Equal([1, 2, 3], _fake.Uploaded);
    }

    [Fact]
    public async Task ReplacesAJobThatIsLoadedButNotStarted()
    {
        _fake.Mode = "P_WORK";
        await using var client = await Connected();
        // Without permission: refused, and the loaded job is left alone.
        var e = await Assert.ThrowsAsync<DeviceException>(() => client.UploadJobAsync([1, 2, 3]));
        Assert.Contains("already loaded", e.Message);
        Assert.Empty(_fake.States);

        _fake.OnStateAction = a => { if (a == "stop") _fake.Mode = "P_IDLE"; };
        await client.UploadJobAsync([1, 2, 3], replaceLoadedJob: true);
        Assert.Equal(["stop"], _fake.States);
        Assert.Equal([1, 2, 3], _fake.Uploaded);
    }

    [Fact]
    public async Task ReplacesALoadedJobAsReportedByFirmware4054()
    {
        // What the F2 Ultra actually reports while a job waits for its button.
        (_fake.Mode, _fake.SubMode, _fake.Desc, _fake.TaskId) = ("Work", "workReady", "normal", "c2d7c087-caf9-4113-920d-0ae7c984aebf");
        await using var client = await Connected();
        var st = await client.GetRuntimeStateAsync();
        Assert.Equal(("P_WORK", "c2d7c087-caf9-4113-920d-0ae7c984aebf"), (st.Mode, st.TaskId));
        _fake.OnStateAction = a => { if (a == "stop") (_fake.Mode, _fake.SubMode, _fake.Desc) = ("P_IDLE", "", "normal"); };
        await client.UploadJobAsync([4, 5, 6], replaceLoadedJob: true);
        Assert.Equal(["stop"], _fake.States);
    }

    [Theory]
    [InlineData("Work", "workReady", "normal", "P_WORK")]
    [InlineData("Work", "working", "normal", "P_WORKING")]
    [InlineData("Work", "workPause", "normal", "P_PAUSE")]
    [InlineData("Work", "working", "frame", "P_FRAMING")]
    [InlineData("Work", "somethingNew", "normal", "Work/somethingNew")]
    [InlineData("P_IDLE", "", "normal", "P_IDLE")]
    [InlineData("P_SLEEP", null, null, "P_SLEEP")]
    public void NormalizesRuntimeModes(string mode, string? sub, string? desc, string expected) =>
        Assert.Equal(expected, DeviceClient.NormalizeMode(mode, sub, desc));

    [Fact]
    public async Task FramesLikeXToolStudio()
    {
        await using var client = await Connected();
        await client.StartFrameAsync("G0X1Y1\nG1X2Y1S64F960000");
        var announce = _fake.Requests.Single(r => Url(r) == "/v1/filetransfer/upload")["data"]!;
        Assert.Equal("tmpFrame.gcode", announce["filename"]!.GetValue<string>());
        Assert.Equal("G0X1Y1\nG1X2Y1S64F960000"u8.ToArray(), _fake.Uploaded);
        var config = _fake.Requests.Single(r => Url(r) == "/v1/processing/upload/config")["data"]!;
        Assert.Equal(("txt", 1, 1, "frame"), (config["fileType"]!.GetValue<string>(), config["autoStart"]!.GetValue<int>(),
            config["loopPrint"]!.GetValue<int>(), config["gcodeType"]!.GetValue<string>()));

        await client.ReplaceFrameAsync("G0X5Y5");
        Assert.Contains(_fake.Requests, r => Url(r) == "/v1/filetransfer/upload" && r["data"]!["filename"]!.GetValue<string>() == "tmpFrameNew.gcode");
        Assert.Equal(1, _fake.Requests.Single(r => Url(r) == "/v1/processing/frame/replace")["data"]!["loopPrint"]!.GetValue<int>());

        await client.StopFrameAsync();
        Assert.Equal(["stop"], _fake.States);
    }

    [Fact]
    public async Task SendingAJobStopsAFrameFirst()
    {
        (_fake.Mode, _fake.SubMode, _fake.Desc) = ("Work", "working", "frame");
        await using var client = await Connected();
        Assert.Equal("P_FRAMING", await client.GetRuntimeModeAsync());
        // Without permission it's refused; with it, the frame is stopped and the job goes up.
        await Assert.ThrowsAsync<DeviceException>(() => client.UploadJobAsync([1]));
        _fake.OnStateAction = a => { if (a == "stop") (_fake.Mode, _fake.SubMode, _fake.Desc) = ("P_IDLE", "", "normal"); };
        await client.UploadJobAsync([1], replaceLoadedJob: true);
        Assert.Equal(["stop"], _fake.States);
    }

    [Fact]
    public async Task NeverFramesOverARunningJob()
    {
        (_fake.Mode, _fake.SubMode) = ("Work", "working");
        await using var client = await Connected();
        await Assert.ThrowsAsync<DeviceException>(() => client.StartFrameAsync("G0X1Y1", replaceLoadedJob: true));
        Assert.Empty(_fake.States);
        Assert.DoesNotContain(_fake.Requests, r => Url(r) == "/v1/filetransfer/upload");
    }

    [Fact]
    public async Task TurnsThePointerLightsOffAndBackLikeXToolStudio()
    {
        await using var client = await Connected();
        Assert.Equal(21, await client.GetFocusPowerAsync());
        await client.SetIrLedAsync(false);
        await client.SetPointerPowerAsync(0);
        await client.SetIrLedAsync(true);
        await client.SetPointerPowerAsync(21);
        var ir = _fake.Requests.Where(r => Url(r) == "/v1/peripheral/param" && r["params"]?["type"]?.GetValue<string>() == "ir_led").ToList();
        Assert.Equal(["off", "on"], ir.Select(r => r["data"]!["action"]!.GetValue<string>()));
        Assert.All(ir, r => Assert.Equal(0, r["data"]!["index"]!.GetValue<int>()));
        var head = _fake.Requests.Where(r => Url(r) == "/v1/laser-head/parameter").Select(r => r["data"]!).ToList();
        Assert.Equal([0.0, 21.0], head.Select(d => d["value"]!.GetValue<double>()));
        Assert.All(head, d => Assert.Equal(("power", "set"), (d["action"]!.GetValue<string>(), d["ctrl"]!.GetValue<string>())));
    }

    [Fact]
    public async Task UsesTheCallersTaskId()
    {
        await using var client = await Connected();
        var id = await client.UploadJobAsync([7, 8, 9], taskId: "chosen-up-front");
        Assert.Equal("chosen-up-front", id);
        var config = _fake.Requests.Single(r => Url(r) == "/v1/processing/upload/config")["data"]!;
        Assert.Equal("chosen-up-front", config["taskId"]!.GetValue<string>());
    }

    [Fact]
    public async Task NeverCancelsARunningJobToUpload()
    {
        (_fake.Mode, _fake.SubMode) = ("Work", "working");
        await using var client = await Connected();
        await Assert.ThrowsAsync<DeviceException>(() => client.UploadJobAsync([1, 2, 3], replaceLoadedJob: true));
        Assert.Empty(_fake.States);
    }

    [Fact]
    public async Task SnapshotDownloadsInWindows()
    {
        var jpeg = new byte[6 * 1024 * 1024 + 123]; // more than one 5 MB window
        new Random(3).NextBytes(jpeg);
        _fake.DownloadFile = jpeg;
        await using var client = await Connected();

        Assert.Equal(jpeg, await client.SnapshotAsync(1));

        var snap = _fake.Requests.Single(r => Url(r) == "/v1/camera/snap");
        Assert.Equal("deep", snap["params"]!["name"]!.GetValue<string>());
        Assert.Equal("1", snap["params"]!["stream"]!.GetValue<string>());
        Assert.Equal(5, _fake.Requests.Single(r => Url(r) == "/v1/filetransfer/download")["data"]!["filetype"]!.GetValue<int>());
        Assert.Equal(2, _fake.WindowRequests);
        var finish = _fake.Requests.Single(r => Url(r) == "/v1/filetransfer/finish")["data"]!;
        Assert.Equal(0, finish["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task DownloadChecksMd5()
    {
        _fake.DownloadFile = new byte[1000];
        _fake.CorruptDigest = true;
        await using var client = await Connected();
        var e = await Assert.ThrowsAsync<DeviceException>(() => client.SnapshotAsync(0));
        Assert.Equal(DeviceException.ChecksumMismatchCode, e.Code);
        var finish = _fake.Requests.Single(r => Url(r) == "/v1/filetransfer/finish")["data"]!;
        Assert.Equal(-9, finish["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task DetectsDeadLink()
    {
        // The dead-link time is 40 heartbeats, so a busy test machine (many tests at once) answering a
        // ping late isn't taken for a dead link; it's still well under the wait below.
        var options = Options() with { HeartbeatInterval = TimeSpan.FromMilliseconds(50), DeadLinkTimeout = TimeSpan.FromSeconds(2) };
        await using var client = await Connected(options);
        var lost = new TaskCompletionSource<DeviceException>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += e => lost.TrySetResult(e);

        // While pings are answered, the link stays up.
        await Task.Delay(800);
        string why = lost.Task.IsCompleted ? "Lost: " + (await lost.Task).Message : "not connected, no Disconnected event";
        Assert.True(client.IsConnected, why);
        Assert.Contains(_fake.Requests, r => Url(r) == "/v1/user/ping" && r["transactionId"]!.GetValue<int>() == DeviceClient.PingTransactionId);

        _fake.AnswerPings = false;
        var pending = client.GetAsync("/v1/test/silent", timeout: TimeSpan.FromSeconds(30));
        var reason = await lost.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(DeviceException.DisconnectedCode, reason.Code);
        Assert.False(client.IsConnected);
        var e = await Assert.ThrowsAsync<DeviceException>(() => pending);
        Assert.Equal(DeviceException.DisconnectedCode, e.Code);
        e = await Assert.ThrowsAsync<DeviceException>(() => client.GetAsync("/v1/device/runtime-infos"));
        Assert.Equal(DeviceException.NotConnectedCode, e.Code);
    }

    [Fact]
    public async Task DetectsClosedSocket()
    {
        await using var client = await Connected();
        var lost = new TaskCompletionSource<DeviceException>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += e => lost.TrySetResult(e);
        await _fake.CloseMainAsync();
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(client.IsConnected);
    }

    static string? Url(JsonObject r) => r["url"]?.GetValue<string>();

    sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        readonly Lock _gate = new();
        public void Report(double value)
        {
            lock (_gate)
                report(value);
        }
    }
}

/// <summary>
/// A minimal in-process imitation of the F2 Ultra's V2 endpoints, served over loopback TCP with
/// raw (non-TLS) WebSockets. Behaviour follows docs/protocol-f2-ultra.md.
/// </summary>
sealed class FakeDevice : IAsyncDisposable
{
    public const int DevicePacketSize = 7000;
    const int UploadWindow = 40_000;

    readonly ConcurrentQueue<JsonObject> _requests = new();
    readonly List<(WebSocket Ws, SemaphoreSlim Lock, TcpClient A, TcpClient B)> _sockets = [];
    readonly System.Threading.Channels.Channel<byte[]> _uploadPackets = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
    readonly CancellationTokenSource _cts = new();
    readonly List<Task> _loops = [];
    WebSocket? _main;
    SemaphoreSlim? _mainLock;
    WebSocket? _file;
    SemaphoreSlim? _fileLock;

    public int ParityCode { get; set; }
    public int AuthStatusPending { get; set; }
    public string Mode { get; set; } = "P_IDLE";
    public string SubMode { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? TaskId { get; set; }
    public Action<string>? OnStateAction { get; set; }
    public Action<string>? OnModeSwitch { get; set; }
    public volatile bool AnswerPings = true;
    public byte[] DownloadFile { get; set; } = [];
    public bool CorruptDigest { get; set; }
    public byte[]? Uploaded { get; private set; }
    public ConcurrentQueue<int> UploadPacketSizes { get; } = new();
    public ConcurrentQueue<string> ConnectionIds { get; } = new();
    public ConcurrentQueue<string> Functions { get; } = new();
    public int WindowRequests;
    bool _gapCheck;
    public double HeadZ = 27.058;
    /// <summary>Event the fake sends after auto_start (null = never answers).</summary>
    public string? FocusOutcome = "FOCUS_FINISHED";
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> Modes = new();
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> States = new();
    public readonly System.Collections.Concurrent.ConcurrentQueue<JsonObject> Lights = new();
    bool _beep = true;

    public IReadOnlyList<JsonObject> Requests => [.. _requests];

    public async Task<WebSocket> ConnectAsync(Uri uri, CancellationToken ct)
    {
        Assert.Equal("wss", uri.Scheme);
        Assert.Equal(DeviceClient.Port, uri.Port);
        Assert.Equal("/websocket", uri.AbsolutePath);
        var query = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
        ConnectionIds.Enqueue(query["id"]);
        string function = query["function"];
        Functions.Enqueue(function);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        var connect = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, ct).AsTask();
        var server = await listener.AcceptTcpClientAsync(ct);
        await connect;
        listener.Stop();
        var clientWs = WebSocket.CreateFromStream(client.GetStream(), new WebSocketCreationOptions { IsServer = false, KeepAliveInterval = TimeSpan.Zero });
        var serverWs = WebSocket.CreateFromStream(server.GetStream(), new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
        var sendLock = new SemaphoreSlim(1, 1);
        lock (_sockets)
            _sockets.Add((serverWs, sendLock, client, server));
        if (function == "instruction")
        {
            _main = serverWs;
            _mainLock = sendLock;
            _loops.Add(Task.Run(() => ServeAsync(serverWs, OnMainFrame)));
        }
        else
        {
            _file = serverWs;
            _fileLock = sendLock;
            _loops.Add(Task.Run(() => ServeAsync(serverWs, OnFileFrame)));
        }
        return clientWs;
    }

    async Task ServeAsync(WebSocket ws, Func<DecodedFrame, Task> onFrame)
    {
        var decoder = new FrameDecoder();
        var buf = new byte[128 * 1024];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var r = await ws.ReceiveAsync(buf.AsMemory(), _cts.Token);
                if (r.MessageType == WebSocketMessageType.Close)
                    return;
                foreach (var f in decoder.Feed(buf.AsSpan(0, r.Count)))
                    await onFrame(f);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
        {
        }
    }

    static async Task SendAsync(WebSocket? ws, SemaphoreSlim? gate, byte[] frame)
    {
        if (ws is null || gate is null)
            return;
        await gate.WaitAsync();
        try
        {
            await ws.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task PushAsync(string json) => SendAsync(_main, _mainLock, Frame.Encode(json));

    public async Task CloseMainAsync()
    {
        if (_main is not null)
            await _main.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    }

    Task Reply(JsonObject req, int code, JsonNode? data = null, string message = "") =>
        SendAsync(_main, _mainLock, Frame.Encode(new JsonObject
        {
            ["type"] = "response",
            ["transactionId"] = req["transactionId"]!.GetValue<int>(),
            ["code"] = code,
            ["message"] = message,
            ["data"] = data ?? new JsonObject(),
        }.ToJsonString()));

    async Task OnMainFrame(DecodedFrame f)
    {
        var req = JsonNode.Parse(f.Payload)!.AsObject();
        _requests.Enqueue(req);
        string url = req["url"]!.GetValue<string>();
        var data = req["data"] as JsonObject;
        switch (url)
        {
            case "/v1/user/ping":
                if (AnswerPings)
                    await Reply(req, 0);
                break;
            case "/v1/user/parity":
                if (ParityCode == 0)
                    await Reply(req, 0, new JsonObject { ["token"] = "new-token" }, "user auth success");
                else
                    await Reply(req, ParityCode, null, "need button");
                break;
            case "/v1/user/auth-status":
                if (AuthStatusPending-- > 0)
                    await Reply(req, 406);
                else
                    await Reply(req, 0, new JsonObject { ["sessionToken"] = "session-1", ["token"] = "button-token" });
                break;
            case "/v1/device/runtime-infos":
                await Reply(req, 0, new JsonObject { ["curMode"] = new JsonObject { ["mode"] = Mode, ["desc"] = Desc, ["subMode"] = SubMode, ["taskId"] = TaskId } });
                break;
            case "/v1/device/machineInfo":
                await Reply(req, 0, JsonNode.Parse("""{"sn":"S1","gcodeFormat":["txt","bin"],"laserType":["RED","BLUE"],"laserPower":[60,40]}"""));
                break;
            case "/v1/processing/progress":
                await Reply(req, 0, new JsonObject { ["progress"] = 2 });
                break;
            case "/v1/laser-head/focus/parameter":
                await Reply(req, 0, new JsonObject { ["value"] = HeadZ });
                break;
            case "/v1/processing/state":
                States.Enqueue(req["params"]!["action"]!.GetValue<string>());
                OnStateAction?.Invoke(req["params"]!["action"]!.GetValue<string>());
                await Reply(req, 0);
                break;
            case "/v1/peripheral/param":
                Lights.Enqueue(req);
                await Reply(req, 0);
                break;
            case "/v1/device/mode":
                Modes.Enqueue(data!["mode"]!.GetValue<string>());
                OnModeSwitch?.Invoke(data!["mode"]!.GetValue<string>());
                await Reply(req, 0);
                break;
            case "/v1/laser-head/focus/control":
                await Reply(req, 0);
                if (data!["action"]!.GetValue<string>() is "auto_start" or "goTo" && FocusOutcome is { } outcome)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        if (outcome == "FOCUS_FINISHED") HeadZ = data["action"]!.GetValue<string>() == "goTo" ? data["z"]!.GetValue<double>() : 9.5;
                        await PushAsync(new JsonObject
                        {
                            ["type"] = "request", ["method"] = "REPORT", ["url"] = "/v1/device/report",
                            ["data"] = new JsonObject { ["module"] = "STATUS_CONTROLLER", ["type"] = outcome, ["info"] = outcome == "FOCUS_STOPED" ? "Button" : "" },
                        }.ToJsonString());
                    });
                break;
            case "/v1/device/configs":
                if (req["method"]!.GetValue<string>() == "PUT")
                {
                    var kv = data!["kv"]!.AsObject();
                    if (kv["gapCheckWithKey"] is { } g) _gapCheck = g.GetValue<bool>();
                    if (kv["beepEnable"] is { } b) _beep = b.GetValue<bool>();
                    await Reply(req, 0);
                }
                else
                {
                    // Reply with only the keys asked for, like the firmware.
                    var reply = new JsonObject();
                    foreach (var key in data!["kv"]!.AsArray().Select(k => k!.GetValue<string>()))
                        reply[key] = key switch { "beepEnable" => _beep, "focusPower" => 21, _ => _gapCheck };
                    await Reply(req, 0, reply);
                }
                break;
            case "/v1/filetransfer/upload":
                await Reply(req, 0, new JsonObject { ["packetsize"] = DevicePacketSize });
                _ = Task.Run(() => PullUploadAsync(data!));
                break;
            case "/v1/processing/upload/config":
            case "/v1/processing/frame/replace":
            case "/v1/laser-head/parameter":
                await Reply(req, 0);
                break;
            case "/v1/camera/snap":
                await Reply(req, 0, new JsonObject { ["filename"] = "/tmp/main_1.jpg" });
                break;
            case "/v1/filetransfer/download":
                string md5 = Convert.ToHexStringLower(MD5.HashData(DownloadFile));
                await Reply(req, 0, new JsonObject { ["filesize"] = DownloadFile.Length, ["digestdata"] = CorruptDigest ? new string('0', 32) : md5.ToUpperInvariant() });
                break;
            case "/v1/filetransfer/finish":
                await Reply(req, 0);
                break;
            case "/v1/test/silent":
                break;
            default:
                await Reply(req, 404, null, "not found");
                break;
        }
    }

    // Device side of an upload: request windows, collect 0x81 packets, then report finish on the main channel.
    async Task PullUploadAsync(JsonObject announce)
    {
        int size = announce["filesize"]!.GetValue<int>();
        int ch = announce["channel"]!.GetValue<int>();
        var file = new byte[size];
        for (int offset = 0; offset < size;)
        {
            int window = Math.Min(UploadWindow, size - offset);
            byte[] req = [0x01, (byte)ch, 0, (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, (byte)(window >> 16), (byte)(window >> 8), (byte)window];
            await SendAsync(_file, _fileLock, Frame.Encode(req, ProtocolType.FileTransfer, bodyCrc: false));
            for (int got = 0; got < window;)
            {
                var pkt = await _uploadPackets.Reader.ReadAsync(_cts.Token);
                Assert.Equal(0x81, pkt[0]);
                Assert.Equal(ch, pkt[1]);
                long pos = ((long)pkt[2] << 32) | ((long)pkt[3] << 24) | ((long)pkt[4] << 16) | ((long)pkt[5] << 8) | pkt[6];
                pkt.AsSpan(7).CopyTo(file.AsSpan((int)pos));
                UploadPacketSizes.Enqueue(pkt.Length - 7);
                got += pkt.Length - 7;
            }
            offset += window;
        }
        bool ok = Convert.ToHexStringLower(MD5.HashData(file)) == announce["digestdata"]!.GetValue<string>();
        Uploaded = file;
        await PushAsync(new JsonObject
        {
            ["type"] = "request",
            ["method"] = "PUT",
            ["url"] = "/v1/filetransfer/finish",
            ["data"] = new JsonObject { ["code"] = ok ? 0 : -8, ["message"] = ok ? "ok" : "md5", ["channel"] = ch },
        }.ToJsonString());
    }

    async Task OnFileFrame(DecodedFrame f)
    {
        Assert.Equal(ProtocolType.FileTransfer, f.Type);
        var p = f.Payload;
        if (p[0] == 0x81)
        {
            await _uploadPackets.Writer.WriteAsync(p);
            return;
        }
        Assert.Equal(0x01, p[0]);
        Interlocked.Increment(ref WindowRequests);
        int ch = p[1];
        long offset = ((long)p[2] << 32) | ((long)p[3] << 24) | ((long)p[4] << 16) | ((long)p[5] << 8) | p[6];
        int window = (p[7] << 16) | (p[8] << 8) | p[9];
        const int chunk = 100_000;
        for (long pos = offset; pos < offset + window; pos += chunk)
        {
            int n = (int)Math.Min(chunk, offset + window - pos);
            var pkt = new byte[7 + n];
            pkt[0] = 0x81;
            pkt[1] = (byte)ch;
            pkt[2] = (byte)(pos >> 32);
            pkt[3] = (byte)(pos >> 24);
            pkt[4] = (byte)(pos >> 16);
            pkt[5] = (byte)(pos >> 8);
            pkt[6] = (byte)pos;
            DownloadFile.AsSpan((int)pos, n).CopyTo(pkt.AsSpan(7));
            await SendAsync(_file, _fileLock, Frame.Encode(pkt, ProtocolType.FileTransfer, bodyCrc: false));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        lock (_sockets)
        {
            foreach (var (ws, _, a, b) in _sockets)
            {
                ws.Abort();
                a.Dispose();
                b.Dispose();
            }
        }
        await Task.WhenAll(_loops).WaitAsync(TimeSpan.FromSeconds(5));
    }
}

public class MachineKeyTests
{
    [Fact]
    public void FindsKeysByFingerprintWithoutKnowingThem()
    {
        // A fake "program file": the keys are just quoted strings among lots of others.
        string a = "aaaaaaaabbbbbbbbccccccccdddddddd", b = "0123456789abcdef0123456789ABCDEF", c = "cGFpcmluZy1rZXktMTIz";
        var filler = string.Concat(Enumerable.Range(0, 200_000).Select(i => $"x{i},\"tok{i:D27}\";"));
        var text = filler + $"k=\"{a}\";" + filler + $"q='{c}';" + $"r=`{b}`" + filler;
        var wanted = new[] { (32, MachineKeys.Fingerprint(a)), (32, MachineKeys.Fingerprint(b)), (20, MachineKeys.Fingerprint(c)) };
        var found = KeyFinder.Scan(new MemoryStream(Encoding.ASCII.GetBytes(text)), wanted, null, default);
        Assert.Equal((a, b, c), (found.DiscoveryRequest, found.DiscoveryReply, found.Pairing));
    }

    [Fact]
    public void KeysAcrossChunkBoundariesAreFound()
    {
        string a = "aaaaaaaabbbbbbbbccccccccdddddddd";
        var wanted = new[] { (32, MachineKeys.Fingerprint(a)), (32, "none"), (20, "none") };
        // Place the key so it straddles the 4 MB read boundary.
        int at = (4 << 20) - 10;
        var bytes = Enumerable.Repeat((byte)' ', at).Concat(Encoding.ASCII.GetBytes($"\"{a}\"")).Concat(Enumerable.Repeat((byte)' ', 100)).ToArray();
        Assert.Equal(a, KeyFinder.Scan(new MemoryStream(bytes), wanted, null, default).DiscoveryRequest);
    }

    [Fact]
    public void ValidatesAgainstFingerprintsAndRoundTrips()
    {
        Assert.False(MachineKeys.Empty.IsComplete);
        Assert.False(new MachineKeys("wrong", "wrong", "wrong").RequestValid);
        var dir = Path.Combine(Path.GetTempPath(), "photoncanvas-keys-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(dir, "keys.json");
            new MachineKeys("1", "2", "3").Save(path);
            Assert.Equal(new MachineKeys("1", "2", "3"), MachineKeys.Load(path));
            Assert.Equal(MachineKeys.Empty, MachineKeys.Load(Path.Combine(dir, "missing.json")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
