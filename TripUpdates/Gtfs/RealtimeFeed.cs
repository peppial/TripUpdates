using System.Collections.Concurrent;

namespace TripUpdates.Gtfs;

/// <summary>
/// Holds the most recent trip-updates payload. The upstream feed is one 560 KB document
/// covering all of Sofia, so it is fetched once per poll regardless of how many devices ask,
/// and parsed at most once per catalog per poll.
/// </summary>
public sealed class RealtimeFeed
{
    private readonly record struct Payload(byte[] Bytes, DateTimeOffset FetchedAt);

    private readonly Lock _gate = new();
    private Payload? _current;
    private string? _lastError;
    private ConcurrentDictionary<string, RealtimeSnapshot> _parsed = new();

    public void Publish(byte[] bytes, DateTimeOffset fetchedAt)
    {
        lock (_gate)
        {
            _current = new Payload(bytes, fetchedAt);
            _lastError = null;
            _parsed = new ConcurrentDictionary<string, RealtimeSnapshot>();
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate) _lastError = error;   // the last good payload is deliberately kept
    }

    public FeedState StateFor(StaticCatalog catalog)
    {
        Payload? payload;
        string? error;
        ConcurrentDictionary<string, RealtimeSnapshot> parsed;

        lock (_gate)
        {
            payload = _current;
            error = _lastError;
            parsed = _parsed;
        }

        if (payload is not { } p) return new FeedState(null, error);

        var key = $"{catalog.RouteId}|{string.Join(",", catalog.AllStopIds.Order(StringComparer.Ordinal))}";
        var snapshot = parsed.GetOrAdd(key, _ =>
            GtfsRealtimeReader.Read(new MemoryStream(p.Bytes, writable: false),
                catalog.RouteId, catalog.AllStopIds, p.FetchedAt));

        return new FeedState(snapshot, error);
    }
}
