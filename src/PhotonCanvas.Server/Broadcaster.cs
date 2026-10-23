using System.Text.Json;
using System.Threading.Channels;

namespace PhotonCanvas.Server;

/// <summary>One message for the phones: its kind ("status", "job", "alert") and its JSON.</summary>
public sealed record ServerEvent(string Kind, string Json);

/// <summary>Hands every event to every phone that is listening (GET /api/events).</summary>
public sealed class Broadcaster
{
    readonly Lock _gate = new();
    readonly List<Channel<ServerEvent>> _listeners = [];

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int Listeners
    {
        get { lock (_gate) return _listeners.Count; }
    }

    /// <summary>Start listening; dispose the returned handle to stop.</summary>
    public Subscription Subscribe()
    {
        // A slow phone loses old messages, never holds up the others.
        var ch = Channel.CreateBounded<ServerEvent>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_gate) _listeners.Add(ch);
        return new Subscription(this, ch);
    }

    public void Publish(string kind, object payload)
    {
        var e = new ServerEvent(kind, JsonSerializer.Serialize(payload, JsonOptions));
        lock (_gate)
            foreach (var ch in _listeners) ch.Writer.TryWrite(e);
    }

    void Remove(Channel<ServerEvent> ch)
    {
        lock (_gate) _listeners.Remove(ch);
        ch.Writer.TryComplete();
    }

    public sealed class Subscription(Broadcaster owner, Channel<ServerEvent> channel) : IDisposable
    {
        public ChannelReader<ServerEvent> Reader => channel.Reader;
        public void Dispose() => owner.Remove(channel);
    }
}
