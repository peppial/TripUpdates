namespace TripUpdates.Configuration;

public sealed class TripUpdatesOptions
{
    public const string SectionName = "TripUpdates";

    public FeedOptions Feed { get; set; } = new();

    public string Line { get; set; } = "";

    public string StopName { get; set; } = "";

    public List<DirectionOptions> Directions { get; set; } = [];
}

public sealed class FeedOptions
{
    public string TripUpdatesUrl { get; set; } = "https://gtfs.sofiatraffic.bg/api/v1/trip-updates";
    public string StaticUrl { get; set; } = "https://gtfs.sofiatraffic.bg/api/v1/static";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan StaticRefreshInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Readings older than this are reported as stale rather than shown as live.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// How far ahead a bus is worth mentioning. Past this the number stops being something you
    /// wait for — on a route whose last bus is followed by a thirteen hour gap, "801 минути" is
    /// noise. Beyond the horizon the direction reads as having no upcoming service.
    /// </summary>
    public TimeSpan Horizon { get; set; } = TimeSpan.FromMinutes(120);

    /// <summary>Where the 19 MB static feed and the resolved catalog are cached.</summary>
    public string CacheDirectory { get; set; } = "cache";
}

public sealed class DirectionOptions
{
    public string Label { get; set; } = "";
    public List<string> Headsigns { get; set; } = [];
}
