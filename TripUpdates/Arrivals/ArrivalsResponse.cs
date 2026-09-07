namespace TripUpdates.Arrivals;

public sealed record DirectionArrival(
    string Label,
    bool HasArrival,
    int? Minutes,
    DateTimeOffset? ArrivesAt,
    string Message)
{
    /// <summary>The bus after next, shown smaller beside the first: "4, 65". Null when there is only one.</summary>
    public int? ThenMinutes { get; init; }

    public DateTimeOffset? ThenArrivesAt { get; init; }

    /// <summary>
    /// True when these times come from the printed timetable rather than a live prediction.
    /// Sofia's realtime feed only carries trips already running, so a bus an hour out has none.
    /// </summary>
    public bool Scheduled { get; init; }

    /// <summary>Same, for the bus after next — which is the common case: live first, printed second.</summary>
    public bool ThenScheduled { get; init; }
}

public sealed record ArrivalsResponse(
    string Line,
    string Stop,
    DateTimeOffset GeneratedAt,
    bool Stale,
    DateTimeOffset? UpdatedAt,
    string? Error,
    IReadOnlyList<DirectionArrival> Directions);
