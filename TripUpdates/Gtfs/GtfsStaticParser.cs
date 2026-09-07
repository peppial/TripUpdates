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

        var headsignByTrip = ReadHeadsigns(zip, routeId);
        if (headsignByTrip.Count == 0)
            throw new GtfsResolutionException($"Line '{line}' ({routeId}) has no trips in the static feed.");

        var headsignsByStop = ReadHeadsignsServingStops(zip, headsignByTrip, stopIds);
        if (headsignsByStop.Count == 0)
            throw new GtfsResolutionException($"Line '{line}' does not serve stop '{stopName}'.");

        return new StaticCatalog(line, routeId, stopName, BuildDirections(headsignsByStop, directionOverrides));
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

    private static Dictionary<string, string> ReadHeadsigns(ZipArchive zip, string routeId)
    {
        var headsigns = new Dictionary<string, string>(StringComparer.Ordinal);
        using var rows = Open(zip, "trips.txt");
        while (rows.Read())
        {
            if (!string.Equals(rows["route_id"], routeId, StringComparison.Ordinal)) continue;
            var headsign = rows["trip_headsign"].Trim();
            if (headsign.Length > 0) headsigns[rows["trip_id"]] = headsign;
        }
        return headsigns;
    }

    /// <summary>Per stop, how many trips call there under each headsign.</summary>
    private static Dictionary<string, Dictionary<string, int>> ReadHeadsignsServingStops(
        ZipArchive zip, Dictionary<string, string> headsignByTrip, HashSet<string> stopIds)
    {
        var byStop = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        using var rows = Open(zip, "stop_times.txt");
        while (rows.Read())
        {
            var stopId = rows["stop_id"];
            if (!stopIds.Contains(stopId)) continue;
            if (!headsignByTrip.TryGetValue(rows["trip_id"], out var headsign)) continue;

            if (!byStop.TryGetValue(stopId, out var counts))
                byStop[stopId] = counts = new Dictionary<string, int>(StringComparer.Ordinal);
            counts[headsign] = counts.GetValueOrDefault(headsign) + 1;
        }
        return byStop;
    }

    private static List<DirectionBinding> BuildDirections(
        Dictionary<string, Dictionary<string, int>> headsignsByStop,
        IReadOnlyList<DirectionOptions> overrides)
    {
        var directions = new List<DirectionBinding>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var option in overrides)
        {
            var wanted = option.Headsigns.Select(Normalize).ToHashSet(StringComparer.Ordinal);
            var stops = headsignsByStop
                .Where(kv => kv.Value.Keys.Any(h => wanted.Contains(Normalize(h))))
                .Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();

            if (stops.Count == 0) continue;

            var headsigns = stops.SelectMany(s => headsignsByStop[s].Keys)
                .Where(h => wanted.Contains(Normalize(h)))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

            directions.Add(new DirectionBinding(option.Label, stops, headsigns));
            claimed.UnionWith(stops);
        }

        // Whatever the settings did not name is still shown. One card per stop, because a stop
        // is one physical direction of travel; stops that end up with the same label are then
        // merged, so a loosely matched stop name cannot produce a wall of identical cards.
        var leftovers = headsignsByStop
            .Where(kv => !claimed.Contains(kv.Key))
            .GroupBy(kv => "към " + ToTitleCase(PrimaryHeadsign(kv.Value)), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in leftovers)
        {
            directions.Add(new DirectionBinding(
                group.Key,
                [.. group.Select(kv => kv.Key).Order(StringComparer.Ordinal)],
                [.. group.SelectMany(kv => kv.Value.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]));
        }

        return directions;
    }

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
