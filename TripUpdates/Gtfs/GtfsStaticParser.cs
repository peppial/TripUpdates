using System.IO.Compression;
using TripUpdates.Configuration;

namespace TripUpdates.Gtfs;

public static class GtfsStaticParser
{
    /// <summary>
    /// Resolves settings (line number, stop name) against a GTFS static feed.
    ///
    /// Direction cannot come from direction_id: it is empty throughout the Sofia feed.
    /// It is derived instead from trip_headsign, joined to the stop via stop_times.
    /// </summary>
    public static StaticCatalog Resolve(
        Stream zipStream,
        string line,
        string stopName,
        IReadOnlyList<DirectionOptions> directionOverrides)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

        var routeId = FindRouteId(zip, line)
            ?? throw new GtfsResolutionException($"Line '{line}' was not found in the static feed.");

        var stopIds = FindStopIds(zip, stopName);
        if (stopIds.Count == 0)
            throw new GtfsResolutionException($"Stop '{stopName}' was not found in the static feed.");

        var tripsById = ReadTrips(zip, routeId);
        if (tripsById.Count == 0)
            throw new GtfsResolutionException($"Line '{line}' ({routeId}) has no trips in the static feed.");

        var stops = ReadStopsServed(zip, tripsById, stopIds);
        if (stops.Count == 0)
            throw new GtfsResolutionException($"Line '{line}' does not serve stop '{stopName}'.");

        var directions = BuildDirections(stops, directionOverrides);

        return new StaticCatalog(line, routeId, stopName, directions)
        {
            TimeZone = ReadTimeZone(zip),
            ServiceDates = ReadServiceDates(zip, ServiceIdsIn(directions)),
        };
    }

    /// <summary>
    /// Timetable times are written in the agency's local time. The fallback matters only for a feed
    /// with no agency.txt; this app watches Sofia, so guessing UTC there would shift every departure.
    /// </summary>
    private static TimeZoneInfo ReadTimeZone(ZipArchive zip)
    {
        var fallback = FindTimeZone("Europe/Sofia") ?? TimeZoneInfo.Utc;
        if (zip.GetEntry("agency.txt") is null) return fallback;

        using var rows = Open(zip, "agency.txt");
        while (rows.Read())
            if (rows["agency_timezone"].Trim() is { Length: > 0 } id)
                return FindTimeZone(id) ?? fallback;

        return fallback;
    }

    private static TimeZoneInfo? FindTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    private static HashSet<string> ServiceIdsIn(List<DirectionBinding> directions) =>
        directions.SelectMany(d => d.Timetable).Select(d => d.ServiceId).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Which services run on which date. The Sofia feed ships no calendar.txt — every operating day
    /// is an explicit calendar_dates row — so a missing file means the caller should assume nothing
    /// is filtered rather than that nothing runs.
    /// </summary>
    private static Dictionary<DateOnly, IReadOnlySet<string>> ReadServiceDates(
        ZipArchive zip, HashSet<string> wanted)
    {
        var dates = new Dictionary<DateOnly, IReadOnlySet<string>>();
        if (wanted.Count == 0 || zip.GetEntry("calendar_dates.txt") is null) return dates;

        using var rows = Open(zip, "calendar_dates.txt");
        while (rows.Read())
        {
            var serviceId = rows["service_id"];
            if (!wanted.Contains(serviceId)) continue;
            if (!DateOnly.TryParseExact(rows["date"].Trim(), "yyyyMMdd", out var date)) continue;

            if (!dates.TryGetValue(date, out var set))
                dates[date] = set = new HashSet<string>(StringComparer.Ordinal);

            var running = (HashSet<string>)set;
            if (rows["exception_type"].Trim() == "1") running.Add(serviceId);
            else running.Remove(serviceId);
        }
        return dates;
    }

    private static string? FindRouteId(ZipArchive zip, string line)
    {
        using var rows = Open(zip, "routes.txt");
        while (rows.Read())
            if (string.Equals(rows["route_short_name"].Trim(), line.Trim(), StringComparison.OrdinalIgnoreCase))
                return rows["route_id"];
        return null;
    }

    /// <summary>
    /// Substring match, because the rider's "Воденичарски механи" is
    /// "ВОДЕНИЧАРСКИ МЕХАНИ-ПО ЖЕЛАНИЕ" in the feed.
    /// </summary>
    private static HashSet<string> FindStopIds(ZipArchive zip, string stopName)
    {
        var needle = Normalize(stopName);
        var found = new HashSet<string>(StringComparer.Ordinal);
        using var rows = Open(zip, "stops.txt");
        while (rows.Read())
            if (Normalize(rows["stop_name"]).Contains(needle, StringComparison.Ordinal))
                found.Add(rows["stop_id"]);
        return found;
    }

    private sealed record TripInfo(string Headsign, string ServiceId);

    /// <summary>What each stop of interest sees: headsign counts, and the printed departures.</summary>
    private sealed class StopSchedule
    {
        public Dictionary<string, int> HeadsignCounts { get; } = new(StringComparer.Ordinal);
        public List<ScheduledDeparture> Departures { get; } = [];
    }

    private static Dictionary<string, TripInfo> ReadTrips(ZipArchive zip, string routeId)
    {
        var trips = new Dictionary<string, TripInfo>(StringComparer.Ordinal);
        using var rows = Open(zip, "trips.txt");
        while (rows.Read())
        {
            if (!string.Equals(rows["route_id"], routeId, StringComparison.Ordinal)) continue;
            var headsign = rows["trip_headsign"].Trim();
            if (headsign.Length > 0) trips[rows["trip_id"]] = new TripInfo(headsign, rows["service_id"]);
        }
        return trips;
    }

    /// <summary>
    /// One pass over stop_times.txt — the 45 MB file — collecting both the headsigns that call at
    /// each watched stop and the timetable for it.
    /// </summary>
    private static Dictionary<string, StopSchedule> ReadStopsServed(
        ZipArchive zip, Dictionary<string, TripInfo> tripsById, HashSet<string> stopIds)
    {
        var byStop = new Dictionary<string, StopSchedule>(StringComparer.Ordinal);
        using var rows = Open(zip, "stop_times.txt");
        while (rows.Read())
        {
            var stopId = rows["stop_id"];
            if (!stopIds.Contains(stopId)) continue;
            var tripId = rows["trip_id"];
            if (!tripsById.TryGetValue(tripId, out var trip)) continue;

            if (!byStop.TryGetValue(stopId, out var schedule))
                byStop[stopId] = schedule = new StopSchedule();

            schedule.HeadsignCounts[trip.Headsign] = schedule.HeadsignCounts.GetValueOrDefault(trip.Headsign) + 1;

            var time = rows["departure_time"] is { Length: > 0 } departure
                ? GtfsTime.Parse(departure)
                : GtfsTime.Parse(rows["arrival_time"]);
            if (time is not null)
                schedule.Departures.Add(new ScheduledDeparture(tripId, trip.ServiceId, time.Value));
        }
        return byStop;
    }

    private static List<DirectionBinding> BuildDirections(
        Dictionary<string, StopSchedule> headsignsByStop,
        IReadOnlyList<DirectionOptions> overrides)
    {
        var directions = new List<DirectionBinding>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var option in overrides)
        {
            var wanted = option.Headsigns.Select(Normalize).ToHashSet(StringComparer.Ordinal);
            var stops = headsignsByStop
                .Where(kv => kv.Value.HeadsignCounts.Keys.Any(h => wanted.Contains(Normalize(h))))
                .Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();

            if (stops.Count == 0) continue;

            var headsigns = stops.SelectMany(s => headsignsByStop[s].HeadsignCounts.Keys)
                .Where(h => wanted.Contains(Normalize(h)))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

            directions.Add(new DirectionBinding(option.Label, stops, headsigns)
            {
                Timetable = Timetable(headsignsByStop, stops),
            });
            claimed.UnionWith(stops);
        }

        // Whatever the settings did not name is still shown. One card per stop, because a stop
        // is one physical direction of travel; stops that end up with the same label are then
        // merged, so a loosely matched stop name cannot produce a wall of identical cards.
        var leftovers = headsignsByStop
            .Where(kv => !claimed.Contains(kv.Key))
            .GroupBy(kv => "към " + ToTitleCase(PrimaryHeadsign(kv.Value.HeadsignCounts)), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in leftovers)
        {
            var stops = group.Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
            directions.Add(new DirectionBinding(
                group.Key,
                stops,
                [.. group.SelectMany(kv => kv.Value.HeadsignCounts.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)])
            {
                Timetable = Timetable(headsignsByStop, stops),
            });
        }

        return directions;
    }

    private static List<ScheduledDeparture> Timetable(
        Dictionary<string, StopSchedule> byStop, IEnumerable<string> stops) =>
        [.. stops.SelectMany(s => byStop[s].Departures).Distinct().OrderBy(d => d.TimeOfDay)];

    /// <summary>The headsign most trips at this stop carry; ties broken alphabetically for stability.</summary>
    private static string PrimaryHeadsign(Dictionary<string, int> counts) =>
        counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;

    private static CsvRows Open(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName)
            ?? throw new GtfsResolutionException($"The static feed is missing {entryName}.");
        return new CsvRows(new StreamReader(entry.Open(), System.Text.Encoding.UTF8));
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string ToTitleCase(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}

public sealed class GtfsResolutionException(string message) : Exception(message);
