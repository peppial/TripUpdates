using TripUpdates.Configuration;
using TripUpdates.Gtfs;

namespace TripUpdates.Arrivals;

public sealed class ArrivalsService(FeedOptions options)
{
    /// <summary>
    /// A bus that was due up to this long ago is still shown as "пристига сега" rather than
    /// dropped: predictions lag reality slightly, and vanishing early is worse than a late zero.
    /// </summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(60);

    public ArrivalsResponse Build(StaticCatalog catalog, FeedState state, DateTimeOffset now)
    {
        var snapshot = state.Snapshot;
        var stale = snapshot is null || now - snapshot.FetchedAt > options.StaleAfter;

        var directions = catalog.Directions
            .Select(d => Build(catalog.Line, d, snapshot, now))
            .ToList();

        return new ArrivalsResponse(
            Line: catalog.Line,
            Stop: catalog.StopName,
            GeneratedAt: now,
            Stale: stale,
            UpdatedAt: snapshot?.FetchedAt,
            Error: state.LastError,
            Directions: directions);
    }

    private static DirectionArrival Build(
        string line, DirectionBinding direction, RealtimeSnapshot? snapshot, DateTimeOffset now)
    {
        var next = NextArrival(direction, snapshot, now);
        if (next is null)
            return new DirectionArrival(direction.Label, false, null, null,
                BulgarianText.NoArrival(line, direction.Label));

        var minutes = (int)Math.Round((next.Value - now).TotalMinutes, MidpointRounding.AwayFromZero);
        minutes = Math.Max(minutes, 0);

        return new DirectionArrival(direction.Label, true, minutes, next.Value,
            BulgarianText.Arrival(line, direction.Label, minutes));
    }

    private static DateTimeOffset? NextArrival(
        DirectionBinding direction, RealtimeSnapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot is null) return null;

        DateTimeOffset? best = null;
        foreach (var stopId in direction.StopIds)
        {
            if (!snapshot.ArrivalsByStop.TryGetValue(stopId, out var times)) continue;
            foreach (var time in times)
            {
                if (time < now - Grace) continue;
                if (best is null || time < best) best = time;
            }
        }
        return best;
    }
}
