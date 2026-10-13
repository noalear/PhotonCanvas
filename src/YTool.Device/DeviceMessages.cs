using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YTool.Device;

/// <summary>A reply on the instruction channel: <c>{"type":"response","transactionId","code","data","message"}</c>.</summary>
public sealed record DeviceResponse(int TransactionId, int Code, string? Message, JsonNode? Data, JsonObject Raw)
{
    public bool IsSuccess => Code == 0;

    /// <summary>Parses a response object. Returns null if it is not <c>type:"response"</c> with a transaction id.</summary>
    public static DeviceResponse? TryParse(JsonObject msg)
    {
        if (Json.Str(msg, "type") != "response" || Json.Int(msg, "transactionId") is not int txid)
            return null;
        // A missing code is treated as an error rather than as success.
        int code = Json.Int(msg, "code") ?? DeviceException.ProtocolErrorCode;
        return new DeviceResponse(txid, code, Json.Str(msg, "message"), msg["data"], msg);
    }
}

/// <summary>
/// A push message from the device. Verified shape (fw 40.54):
/// <c>{"type":"request","method":"REPORT|NOTIFY","url":...,"data":{"module","type","info","level"}}</c>.
/// The flat shape <c>{module,type,info,level}</c> is also accepted.
/// </summary>
/// <param name="Module">e.g. <c>MODE_CHANGE</c>'s module, <c>BUTTON</c>, <c>GAP</c>, <c>WORK_RESULT</c>.</param>
/// <param name="Type">e.g. <c>P_IDLE</c>, <c>WORK_STARTED</c>, <c>WORK_FINISHED</c>, <c>SHOERT_PRESS</c> (sic), <c>OPEN</c>.</param>
/// <param name="Info">Free-form payload (string, number or object, e.g. <c>{result, timeUse}</c>).</param>
/// <param name="Level">Level as text (the device may send a number or a string).</param>
/// <param name="Url">The request url of the push, if any.</param>
/// <param name="Method">REPORT, NOTIFY, ...</param>
/// <param name="Raw">The whole message.</param>
public sealed record DeviceEvent(string? Module, string? Type, JsonNode? Info, string? Level, string? Url, string? Method, JsonObject Raw)
{
    /// <summary>Info as text, whether the device sent a string or a number (e.g. button id "1").</summary>
    public string? InfoText => Info is JsonValue v ? Json.ValueText(v) : Info?.ToJsonString();

    public static DeviceEvent Parse(JsonObject msg)
    {
        // Nested shape: the event is in "data". Flat shape: the top level has "module"; then
        // top-level "type" is the event type, otherwise it is the envelope's "request".
        bool nested = msg["data"] is JsonObject d && (d.ContainsKey("type") || d.ContainsKey("module"));
        JsonObject body = nested ? (JsonObject)msg["data"]! : msg;
        bool hasEventType = nested || msg.ContainsKey("module");
        string? level = body["level"] is JsonValue lv ? Json.ValueText(lv) : null;
        return new DeviceEvent(
            Module: Json.Str(body, "module"),
            Type: hasEventType ? Json.Str(body, "type") : null,
            Info: body["info"],
            Level: level,
            Url: Json.Str(msg, "url"),
            Method: Json.Str(msg, "method"),
            Raw: msg);
    }

    /// <summary>Parses UTF-8 JSON; null if it isn't a JSON object.</summary>
    public static DeviceEvent? TryParse(ReadOnlySpan<byte> utf8Json) =>
        Json.TryParseObject(utf8Json) is { } o ? Parse(o) : null;
}

/// <summary>Selected fields of <c>GET /v1/device/machineInfo</c>; everything is in <see cref="Raw"/>.</summary>
public sealed record MachineInfo(
    string? Sn,
    string? Firmware,
    IReadOnlyList<string> LaserType,
    IReadOnlyList<double> LaserPower,
    IReadOnlyList<string> GcodeFormat,
    IReadOnlyList<string> PackageFormat,
    JsonObject Raw)
{
    /// <summary>True when the device accepts plain-text G-code (<c>gcodeFormat</c> contains "txt").</summary>
    public bool AcceptsTextGcode => GcodeFormat.Contains("txt");

    public static MachineInfo Parse(JsonObject raw) => new(
        Sn: Json.Str(raw, "sn") ?? Json.Str(raw, "deviceSn") ?? Json.Str(raw, "sncode"),
        // Firmware 40.54 nests it: "firmware": {"package_version": "40.54.021.00.ht6", ...}.
        Firmware: Json.Str(raw, "firmware") ?? Json.Str(raw["firmware"] as JsonObject, "package_version")
                  ?? Json.Str(raw, "firmwareVersion") ?? Json.Str(raw, "version"),
        LaserType: Json.Strings(raw["laserType"]),
        LaserPower: Json.Numbers(raw["laserPower"]),
        GcodeFormat: Json.Strings(raw["gcodeFormat"]),
        PackageFormat: Json.Strings(raw["packageFormat"]),
        Raw: raw);
}

/// <summary>Tolerant JSON accessors: the firmware is loose about strings vs numbers.</summary>
internal static class Json
{
    public static JsonObject? TryParseObject(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return JsonNode.Parse(utf8) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static string? ValueText(JsonValue v) => v.GetValueKind() switch
    {
        JsonValueKind.String => v.GetValue<string>(),
        JsonValueKind.Number => v.ToJsonString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    public static string? Str(JsonObject? obj, string key) =>
        obj?[key] is JsonValue v && ValueText(v) is { Length: > 0 } s ? s : null;

    public static double? Number(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        return v.GetValueKind() switch
        {
            JsonValueKind.Number => v.GetValue<double>(),
            JsonValueKind.String when double.TryParse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => d,
            _ => null,
        };
    }

    public static int? Int(JsonObject? obj, string key) =>
        Number(obj?[key]) is double d && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue ? (int)d : null;

    public static long? Long(JsonObject? obj, string key) =>
        Number(obj?[key]) is double d && d == Math.Floor(d) && d >= 0 && d <= 1e15 ? (long)d : null;

    public static bool? Bool(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        return v.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.GetValue<double>() != 0,
            JsonValueKind.String => v.GetValue<string>() switch
            {
                "true" or "1" => true,
                "false" or "0" => false,
                _ => null,
            },
            _ => null,
        };
    }

    public static IReadOnlyList<string> Strings(JsonNode? node) =>
        node is JsonArray a ? [.. a.OfType<JsonValue>().Select(ValueText).OfType<string>()] : [];

    public static IReadOnlyList<double> Numbers(JsonNode? node) =>
        node is JsonArray a ? [.. a.Select(Number).OfType<double>()] : [];
}
