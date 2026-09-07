namespace TripUpdates.Gtfs;

/// <summary>One rider-facing direction, and the stop_ids that serve it.</summary>
public sealed record DirectionBinding(string Label, IReadOnlyList<string> StopIds, IReadOnlyList<string> Headsigns);

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
}
