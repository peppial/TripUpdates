namespace TripUpdates.Gtfs;

/// <summary>Arrival times for the watched stops, as of one successful poll.</summary>
public sealed record RealtimeSnapshot(
    IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> ArrivalsByStop,
    DateTimeOffset FetchedAt,
    DateTimeOffset? FeedTimestamp)
{
    public static RealtimeSnapshot Empty(DateTimeOffset at) =>
        new(new Dictionary<string, IReadOnlyList<DateTimeOffset>>(), at, null);
}

/// <summary>
/// The poller's view of the world: the newest good reading, plus whatever went wrong since.
/// A failed poll never discards a good snapshot — it is reported as stale instead.
/// </summary>
public sealed record FeedState(RealtimeSnapshot? Snapshot, string? LastError)
{
    public static readonly FeedState Initial = new(null, null);
}
