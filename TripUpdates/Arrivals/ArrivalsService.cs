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
            .Select(d => Build(catalog, d, snapshot, now))
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

    private DirectionArrival Build(
        StaticCatalog catalog, DirectionBinding direction, RealtimeSnapshot? snapshot, DateTimeOffset now)
    {
        var line = catalog.Line;
        var slots = NextTwo(catalog, direction, snapshot, now);

        if (slots.Count == 0)
            return new DirectionArrival(direction.Label, false, null, null,
                BulgarianText.NoArrival(line, direction.Label));

        var first = slots[0];
        var minutes = MinutesUntil(first.At, now);

        Slot? second = slots.Count > 1 ? slots[1] : null;
        int? thenMinutes = second is null ? null : MinutesUntil(second.Value.At, now);

        return new DirectionArrival(direction.Label, true, minutes, first.At,
            BulgarianText.Arrival(
                line, direction.Label, minutes, thenMinutes, first.Scheduled, second?.Scheduled ?? false))
        {
            ThenMinutes = thenMinutes,
            ThenArrivesAt = second?.At,
            Scheduled = first.Scheduled,
            ThenScheduled = second?.Scheduled ?? false,
        };
    }

    private readonly record struct Slot(DateTimeOffset At, bool Scheduled);

    /// <summary>
    /// The next two buses, merging live predictions with the printed timetable in time order.
    ///
    /// They cannot simply be concatenated. A trip enters Sofia's realtime feed only once it starts
    /// running, so realtime routinely knows about a later bus while the one due sooner is still
    /// nothing but a timetable row — appending the timetable behind the live times would hide the
    /// very bus the rider is waiting for.
    /// </summary>
    private List<Slot> NextTwo(
        StaticCatalog catalog, DirectionBinding direction, RealtimeSnapshot? snapshot, DateTimeOffset now)
    {
        var horizon = now + options.Horizon;
        var live = Upcoming(direction, snapshot, now).Select(at => new Slot(at, false));

        // Anything realtime already tracks is dropped from the timetable side, so one bus cannot
        // appear twice — once as observed, once as printed.
        var printed = catalog
            .ScheduledAfter(direction, now, snapshot?.CoveredTripIds)
            .TakeWhile(at => at <= horizon)
            .Take(2)
            .Select(at => new Slot(at, true));

        // Trip ids are the primary defence against showing one bus twice, but they only match while
        // both feeds agree on them. Collapsing slots that land on the same displayed minute — live
        // winning the tie — means a mismatch degrades to one correct row, never "21, 21".
        return [.. live.Concat(printed)
            .Where(s => s.At <= horizon)
            .OrderBy(s => s.At)
            .ThenBy(s => s.Scheduled)
            .DistinctBy(s => MinutesUntil(s.At, now))
            .Take(2)];
    }

    private static int MinutesUntil(DateTimeOffset arrival, DateTimeOffset now) =>
        Math.Max((int)Math.Round((arrival - now).TotalMinutes, MidpointRounding.AwayFromZero), 0);

    /// <summary>
    /// The soonest two arrivals for the direction, across every stop id bound to it. Only two are
    /// needed — the card shows the next bus and, smaller, the one after it.
    /// </summary>
    private static List<DateTimeOffset> Upcoming(
        DirectionBinding direction, RealtimeSnapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot is null) return [];

        var cutoff = now - Grace;
        return direction.StopIds
            .SelectMany(id => snapshot.ArrivalsByStop.TryGetValue(id, out var times)
                ? times
                : [])
            .Where(time => time >= cutoff)
            .Order()
            .Take(2)
            .ToList();
    }
}
