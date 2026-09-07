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

    /// <summary>Where the 19 MB static feed and the resolved catalog are cached.</summary>
    public string CacheDirectory { get; set; } = "cache";
}

public sealed class DirectionOptions
{
    public string Label { get; set; } = "";
    public List<string> Headsigns { get; set; } = [];
}
