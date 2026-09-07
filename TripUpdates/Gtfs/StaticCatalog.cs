namespace TripUpdates.Gtfs;

/// <summary>
/// One printed departure: the time since its service day began, and which service it belongs to.
/// Kept relative rather than absolute because a catalog outlives the day it was resolved on —
/// the static feed only refreshes daily, and an unchanged feed is not re-resolved at all.
/// </summary>
public sealed record ScheduledDeparture(string TripId, string ServiceId, TimeSpan TimeOfDay);

/// <summary>One rider-facing direction, and the stop_ids that serve it.</summary>
public sealed record DirectionBinding(string Label, IReadOnlyList<string> StopIds, IReadOnlyList<string> Headsigns)
{
    /// <summary>The printed timetable for this direction, used when realtime has no prediction.</summary>
    public IReadOnlyList<ScheduledDeparture> Timetable { get; init; } = [];
}

/// <summary>
/// Line and stop names from settings, resolved to the GTFS ids the realtime feed speaks in.
/// Resolved by name on every static refresh: the feed is regenerated periodically and ids change.
/// </summary>
public sealed record StaticCatalog(
    string Line,
    string RouteId,
    string StopName,
    IReadOnlyList<DirectionBinding> Directions)
{
    public IReadOnlySet<string> AllStopIds { get; } =
        Directions.SelectMany(d => d.StopIds).ToHashSet(StringComparer.Ordinal);

    /// <summary>The agency's timezone, which is what timetable times are written in.</summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Which services run on which date. Empty when the feed carries no calendar, in which case
    /// every service is assumed to run every day rather than silently hiding the timetable.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, IReadOnlySet<string>> ServiceDates { get; init; } =
        new Dictionary<DateOnly, IReadOnlySet<string>>();

    /// <summary>
    /// The direction's printed departures that are still ahead of <paramref name="now"/>, soonest first.
    /// </summary>
    public IEnumerable<DateTimeOffset> ScheduledAfter(
        DirectionBinding direction, DateTimeOffset now, IReadOnlySet<string>? excludeTrips = null)
    {
        if (direction.Timetable.Count == 0) return [];

        var localNow = TimeZoneInfo.ConvertTime(now, TimeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);

        // Yesterday is included because GTFS writes a 00:40 bus as "24:40:00" on the day before.
        // Distinct, because several services can run the same day carrying the same departure —
        // two identical times on a card read as two buses when there is only one.
        return Enumerable.Range(-1, 3)
            .Select(today.AddDays)
            .SelectMany(date => DeparturesOn(direction, date, excludeTrips))
            .Where(at => at > now)
            .Distinct()
            .Order();
    }

    private IEnumerable<DateTimeOffset> DeparturesOn(
        DirectionBinding direction, DateOnly serviceDate, IReadOnlySet<string>? excludeTrips)
    {
        var running = RunningOn(serviceDate);
        var startOfDay = StartOfServiceDay(serviceDate);

        return direction.Timetable
            .Where(d => running is null || running.Contains(d.ServiceId))
            .Where(d => excludeTrips is null || !excludeTrips.Contains(d.TripId))
            .Select(d => startOfDay + d.TimeOfDay);
    }

    /// <summary>Null means "no calendar in the feed", which is treated as everything running.</summary>
    private IReadOnlySet<string>? RunningOn(DateOnly date) =>
        ServiceDates.Count == 0
            ? null
            : ServiceDates.GetValueOrDefault(date, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// GTFS measures a service day from noon minus twelve hours, so a day that gains or loses an
    /// hour to daylight saving still has its timetable land on the right wall-clock times.
    /// </summary>
    private DateTimeOffset StartOfServiceDay(DateOnly date)
    {
        var noon = date.ToDateTime(new TimeOnly(12, 0));
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeZone.GetUtcOffset(noon));
    }
}
