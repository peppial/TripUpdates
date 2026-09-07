using TripUpdates.Arrivals;
using TripUpdates.Configuration;
using TripUpdates.Gtfs;

namespace TripUpdates.Tests;

public class ArrivalsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly StaticCatalog Catalog = new("66", "A63", "Воденичарски механи",
    [
        new DirectionBinding("към Алеко", ["A1471"], ["ХИЖА АЛЕКО"]),
        new DirectionBinding("към София", ["A1472"], ["МЕТРОСТАНЦИЯ ВИТОША", "ЗООПАРКА"]),
    ]);

    private static ArrivalsService Service(TimeSpan? staleAfter = null) =>
        new(new FeedOptions { StaleAfter = staleAfter ?? TimeSpan.FromSeconds(120) });

    private static FeedState StateWith(DateTimeOffset fetchedAt, params (string Stop, DateTimeOffset At)[] arrivals)
    {
        var byStop = arrivals
            .GroupBy(a => a.Stop)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DateTimeOffset>)[.. g.Select(a => a.At).Order()]);
        return new FeedState(new RealtimeSnapshot(byStop, fetchedAt, fetchedAt), null);
    }

    /// <summary>
    /// The captured feed has a live prediction only for the downhill direction — Sofia's realtime
    /// feed carries running trips only. Uphill has to come from the printed timetable, which is the
    /// case that used to read "няма предстоящи курсове" while a bus was on its way.
    /// </summary>
    [Fact]
    public void Produces_the_two_messages_from_the_real_captured_feed()
    {
        using var zip = Fixtures.StaticFeed();
        var catalog = GtfsStaticParser.Resolve(zip, "66", "Воденичарски механи",
        [
            new() { Label = "към Алеко", Headsigns = ["ХИЖА АЛЕКО"] },
            new() { Label = "към София", Headsigns = ["МЕТРОСТАНЦИЯ ВИТОША", "ЗООПАРКА"] },
        ]);

        using var pb = Fixtures.TripUpdates();
        var snapshot = GtfsRealtimeReader.Read(pb, catalog.RouteId, catalog.AllStopIds, Fixtures.FeedCapturedAt);
        var response = Service().Build(catalog, new FeedState(snapshot, null), Fixtures.FeedCapturedAt);

        var aleko = response.Directions[0];
        var sofia = response.Directions[1];

        // Captured 18:42 Sofia: the last uphill bus of the day leaves at 20:16 (+93). The one after
        // it is the first of the next morning, 783 minutes out — past the horizon, so it is not shown.
        Assert.True(aleko.Scheduled);
        Assert.Null(aleko.ThenMinutes);
        Assert.Equal("66 към Алеко по разписание след 93 минути", aleko.Message);

        Assert.False(sofia.Scheduled);
        Assert.StartsWith("66 към София ще дойде след 43 минути", sofia.Message);
    }

    [Fact]
    public void Reports_the_soonest_arrival_for_a_direction()
    {
        var state = StateWith(Now,
            ("A1471", Now.AddMinutes(17)),
            ("A1471", Now.AddMinutes(4)),
            ("A1471", Now.AddMinutes(9)));

        var aleko = Service().Build(Catalog, state, Now).Directions[0];

        Assert.Equal(4, aleko.Minutes);
        Assert.Equal(Now.AddMinutes(4), aleko.ArrivesAt);
    }

    [Fact]
    public void Reports_the_bus_after_next_as_well()
    {
        var state = StateWith(Now,
            ("A1471", Now.AddMinutes(17)),
            ("A1471", Now.AddMinutes(4)),
            ("A1471", Now.AddMinutes(9)));

        var aleko = Service().Build(Catalog, state, Now).Directions[0];

        Assert.Equal(4, aleko.Minutes);
        Assert.Equal(9, aleko.ThenMinutes);
        Assert.Equal(Now.AddMinutes(9), aleko.ThenArrivesAt);
        Assert.Equal("66 към Алеко ще дойде след 4 минути, следващият след 9 минути", aleko.Message);
    }

    [Fact]
    public void Leaves_the_second_slot_empty_when_only_one_bus_is_coming()
    {
        var state = StateWith(Now, ("A1471", Now.AddMinutes(6)));
        var aleko = Service().Build(Catalog, state, Now).Directions[0];

        Assert.Equal(6, aleko.Minutes);
        Assert.Null(aleko.ThenMinutes);
        Assert.Null(aleko.ThenArrivesAt);
        Assert.Equal("66 към Алеко ще дойде след 6 минути", aleko.Message);
    }

    [Fact]
    public void Pairs_the_two_soonest_across_several_stops_serving_one_direction()
    {
        var merged = new StaticCatalog("66", "A63", "Воденичарски механи",
            [new DirectionBinding("към София", ["A1472", "A1473"], ["ЗООПАРКА"])]);
        var state = StateWith(Now,
            ("A1472", Now.AddMinutes(12)),
            ("A1473", Now.AddMinutes(3)),
            ("A1472", Now.AddMinutes(7)));

        var sofia = Service().Build(merged, state, Now).Directions[0];

        Assert.Equal(3, sofia.Minutes);
        Assert.Equal(7, sofia.ThenMinutes);
    }

    [Fact]
    public void Does_not_count_a_past_bus_as_the_one_after_next()
    {
        var state = StateWith(Now, ("A1471", Now.AddMinutes(-5)), ("A1471", Now.AddMinutes(8)));
        var aleko = Service().Build(Catalog, state, Now).Directions[0];

        Assert.Equal(8, aleko.Minutes);
        Assert.Null(aleko.ThenMinutes);
    }

    [Fact]
    public void Rounds_to_the_nearest_minute()
    {
        var state = StateWith(Now, ("A1471", Now.AddSeconds(150)));   // 2.5 min
        Assert.Equal(3, Service().Build(Catalog, state, Now).Directions[0].Minutes);
    }

    [Fact]
    public void Shows_a_bus_that_is_barely_overdue_as_arriving_now()
    {
        // Predictions lag reality; dropping the bus 20s early is worse than showing a late zero.
        var state = StateWith(Now, ("A1471", Now.AddSeconds(-20)));
        var aleko = Service().Build(Catalog, state, Now).Directions[0];

        Assert.True(aleko.HasArrival);
        Assert.Equal(0, aleko.Minutes);
        Assert.Equal("66 към Алеко пристига сега", aleko.Message);
    }

    [Fact]
    public void Drops_arrivals_that_are_well_in_the_past()
    {
        var state = StateWith(Now, ("A1471", Now.AddMinutes(-5)));
        Assert.False(Service().Build(Catalog, state, Now).Directions[0].HasArrival);
    }

    [Fact]
    public void Keeps_directions_independent()
    {
        var state = StateWith(Now, ("A1472", Now.AddMinutes(8)));
        var response = Service().Build(Catalog, state, Now);

        Assert.False(response.Directions[0].HasArrival);
        Assert.Equal(8, response.Directions[1].Minutes);
    }

    [Fact]
    public void Takes_the_soonest_across_several_stops_serving_one_direction()
    {
        var merged = new StaticCatalog("66", "A63", "Воденичарски механи",
            [new DirectionBinding("към София", ["A1472", "A1473"], ["ЗООПАРКА"])]);
        var state = StateWith(Now, ("A1472", Now.AddMinutes(12)), ("A1473", Now.AddMinutes(3)));

        Assert.Equal(3, Service().Build(merged, state, Now).Directions[0].Minutes);
    }

    [Fact]
    public void Marks_a_reading_stale_once_it_is_older_than_configured()
    {
        var state = StateWith(Now.AddSeconds(-200), ("A1471", Now.AddMinutes(5)));
        Assert.True(Service(TimeSpan.FromSeconds(120)).Build(Catalog, state, Now).Stale);
    }

    [Fact]
    public void Does_not_mark_a_fresh_reading_stale()
    {
        var state = StateWith(Now.AddSeconds(-20), ("A1471", Now.AddMinutes(5)));
        Assert.False(Service().Build(Catalog, state, Now).Stale);
    }

    [Fact]
    public void Is_stale_and_empty_before_the_first_successful_poll()
    {
        var response = Service().Build(Catalog, FeedState.Initial, Now);

        Assert.True(response.Stale);
        Assert.Null(response.UpdatedAt);
        Assert.All(response.Directions, d => Assert.False(d.HasArrival));
    }

    [Fact]
    public void Surfaces_the_last_error_alongside_the_last_good_reading()
    {
        var snapshot = StateWith(Now.AddSeconds(-45), ("A1471", Now.AddMinutes(6))).Snapshot;
        var response = Service().Build(Catalog, new FeedState(snapshot, "timeout"), Now);

        Assert.Equal("timeout", response.Error);
        Assert.Equal(Now.AddSeconds(-45), response.UpdatedAt);
        Assert.Equal(6, response.Directions[0].Minutes);
    }
}
