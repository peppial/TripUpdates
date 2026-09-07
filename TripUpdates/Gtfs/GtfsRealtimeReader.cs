using Google.Protobuf;
using TransitRealtime;

namespace TripUpdates.Gtfs;

public static class GtfsRealtimeReader
{
    /// <summary>
    /// Reads a GTFS-realtime TripUpdates feed, keeping only arrivals for one route at the
    /// watched stops. The upstream feed carries every trip in Sofia (~780 entities, 560 KB),
    /// so everything else is discarded here rather than held in memory.
    /// </summary>
    public static RealtimeSnapshot Read(
        Stream protobuf, string routeId, IReadOnlySet<string> stopIds, DateTimeOffset fetchedAt)
    {
        var feed = FeedMessage.Parser.ParseFrom(protobuf);
        var arrivals = new Dictionary<string, List<DateTimeOffset>>(StringComparer.Ordinal);
        var coveredTrips = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in feed.Entity)
        {
            var update = entity.TripUpdate;
            if (update is null) continue;
            if (!string.Equals(update.Trip?.RouteId, routeId, StringComparison.Ordinal)) continue;

            foreach (var stopTime in update.StopTimeUpdate)
            {
                if (!stopIds.Contains(stopTime.StopId)) continue;

                var time = stopTime.Arrival?.Time ?? stopTime.Departure?.Time;
                if (time is not > 0) continue;

                if (!arrivals.TryGetValue(stopTime.StopId, out var list))
                    arrivals[stopTime.StopId] = list = [];
                list.Add(DateTimeOffset.FromUnixTimeSeconds(time.Value));

                if (update.Trip?.TripId is { Length: > 0 } tripId) coveredTrips.Add(tripId);
            }
        }

        var byStop = arrivals.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DateTimeOffset>)[.. kv.Value.Order()],
            StringComparer.Ordinal);

        var feedTimestamp = feed.Header?.Timestamp is > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)feed.Header.Timestamp)
            : (DateTimeOffset?)null;

        return new RealtimeSnapshot(byStop, fetchedAt, feedTimestamp) { CoveredTripIds = coveredTrips };
    }
}
