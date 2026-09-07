using TripUpdates.Configuration;
using TripUpdates.Gtfs;

namespace TripUpdates.Tests;

public class GtfsRealtimeReaderTests
{
    private static readonly HashSet<string> Stops = ["A1471", "A1472"];

    private static RealtimeSnapshot Read(string routeId = "A63", IReadOnlySet<string>? stops = null)
    {
        using var pb = Fixtures.TripUpdates();
        return GtfsRealtimeReader.Read(pb, routeId, stops ?? Stops, Fixtures.FeedCapturedAt);
    }

    [Fact]
    public void Reads_the_feed_publication_timestamp()
    {
        Assert.Equal(Fixtures.FeedCapturedAt, Read().FeedTimestamp);
    }

    [Fact]
    public void Keeps_only_the_watched_stops()
    {
        Assert.All(Read().ArrivalsByStop.Keys, id => Assert.Contains(id, Stops));
    }

    [Fact]
    public void Returns_arrivals_sorted_earliest_first()
    {
        foreach (var times in Read().ArrivalsByStop.Values)
            Assert.Equal(times.Order(), times);
    }

    [Fact]
    public void Ignores_other_routes()
    {
        Assert.Empty(Read(routeId: "A12").ArrivalsByStop);
    }

    [Fact]
    public void Ignores_stops_outside_the_catalog()
    {
        Assert.Empty(Read(stops: new HashSet<string> { "A9999" }).ArrivalsByStop);
    }
}
