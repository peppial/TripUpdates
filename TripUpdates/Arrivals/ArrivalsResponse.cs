namespace TripUpdates.Arrivals;

public sealed record DirectionArrival(
    string Label,
    bool HasArrival,
    int? Minutes,
    DateTimeOffset? ArrivesAt,
    string Message);

public sealed record ArrivalsResponse(
    string Line,
    string Stop,
    DateTimeOffset GeneratedAt,
    bool Stale,
    DateTimeOffset? UpdatedAt,
    string? Error,
    IReadOnlyList<DirectionArrival> Directions);
