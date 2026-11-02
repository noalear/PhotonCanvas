namespace PhotonCanvas.Tests;

/// <summary>
/// Tests that wait on real time (heartbeats, a fake machine answering over a socket, the demo
/// remote's job clock) run on their own, after the rest: with heavy tests (slicing, heat plans)
/// running beside them, a thread can stall for seconds and look like a dead link.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Real-time tests";
}
