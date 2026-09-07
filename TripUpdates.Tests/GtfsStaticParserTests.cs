using TripUpdates.Configuration;
using TripUpdates.Gtfs;

namespace TripUpdates.Tests;

public class GtfsStaticParserTests
{
    private static readonly List<DirectionOptions> Sofia66 =
    [
        new() { Label = "към Алеко", Headsigns = ["ХИЖА АЛЕКО"] },
        new() { Label = "към София", Headsigns = ["МЕТРОСТАНЦИЯ ВИТОША", "ЗООПАРКА"] },
    ];

    private static StaticCatalog Resolve(
        string line = "66", string stop = "Воденичарски механи", List<DirectionOptions>? directions = null)
    {
        using var zip = Fixtures.StaticFeed();
        return GtfsStaticParser.Resolve(zip, line, stop, directions ?? Sofia66);
    }

    [Fact]
    public void Reads_the_printed_timetable_for_each_direction()
    {
        var aleko = Resolve().Directions.Single(d => d.Label == "към Алеко");

        Assert.NotEmpty(aleko.Timetable);
        Assert.All(aleko.Timetable, d => Assert.NotEmpty(d.ServiceId));

        // Departures are the ones written against this direction's stop, not the whole route.
        Assert.Contains(aleko.Timetable, d => d.TimeOfDay == new TimeSpan(18, 16, 0));
    }

    [Fact]
    public void Keeps_the_two_directions_timetables_apart()
    {
        var catalog = Resolve();
        var aleko = catalog.Directions.Single(d => d.Label == "към Алеко");
        var sofia = catalog.Directions.Single(d => d.Label == "към София");

        Assert.NotEqual(
            aleko.Timetable.Select(d => d.TimeOfDay).Order().ToArray(),
            sofia.Timetable.Select(d => d.TimeOfDay).Order().ToArray());
    }

    [Fact]
    public void Assumes_every_service_runs_when_the_feed_carries_no_calendar()
    {
        // The trimmed fixture has no calendar_dates.txt. Hiding the whole timetable in that case
        // would be worse than showing it.
        var catalog = Resolve();

        Assert.Empty(catalog.ServiceDates);
        Assert.NotEmpty(catalog.ScheduledAfter(
            catalog.Directions[0], new DateTimeOffset(2026, 9, 7, 0, 1, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Resolves_line_number_to_route_id()
    {
        Assert.Equal("A63", Resolve().RouteId);
    }

    [Fact]
    public void Matches_stop_name_as_substring_because_feed_appends_po_zhelanie()
    {
        // The feed calls it "ВОДЕНИЧАРСКИ МЕХАНИ-ПО ЖЕЛАНИЕ"; the rider does not.
        var catalog = Resolve(stop: "Воденичарски механи");
        Assert.Equal(["A1471", "A1472"], catalog.AllStopIds.Order().ToArray());
    }

    [Fact]
    public void Splits_the_stop_into_one_direction_per_side_of_the_road()
    {
        var catalog = Resolve();

        var aleko = catalog.Directions.Single(d => d.Label == "към Алеко");
        var sofia = catalog.Directions.Single(d => d.Label == "към София");

        Assert.Equal(["A1471"], aleko.StopIds);
        Assert.Equal(["A1472"], sofia.StopIds);
    }

    [Fact]
    public void Merges_several_headsigns_into_one_label()
    {
        // Buses heading down the mountain are signed ЗООПАРКА or МЕТРОСТАНЦИЯ ВИТОША.
        // The rider only cares that they go towards town.
        var sofia = Resolve().Directions.Single(d => d.Label == "към София");
        Assert.Equal(["ЗООПАРКА", "МЕТРОСТАНЦИЯ ВИТОША"], sofia.Headsigns.Order().ToArray());
    }

    [Fact]
    public void Keeps_directions_in_the_order_they_were_configured()
    {
        Assert.Equal(["към Алеко", "към София"], Resolve().Directions.Select(d => d.Label).ToArray());
    }

    [Fact]
    public void Labels_unconfigured_headsigns_from_the_headsign_itself()
    {
        var catalog = Resolve(directions: []);

        Assert.Equal(2, catalog.Directions.Count);
        Assert.Contains(catalog.Directions, d => d.Label == "към Хижа алеко");
    }

    [Fact]
    public void Groups_unconfigured_stops_that_share_a_headsign_into_one_direction()
    {
        // "БЛ. 2" matches two stops on line 102, both signed СТУДЕНТСКИ ГРАД. A rider wants
        // one answer for that direction, not one card per stop_id.
        var catalog = Resolve(line: "102", stop: "БЛ. 2", directions: []);

        var direction = Assert.Single(catalog.Directions);
        Assert.Equal("към Студентски град", direction.Label);
        Assert.Equal(["A0123", "A0174"], direction.StopIds.Order().ToArray());
    }

    [Fact]
    public void Never_shows_the_same_label_twice()
    {
        var labels = Resolve(line: "102", stop: "БЛ. 2", directions: []).Directions.Select(d => d.Label);
        Assert.Distinct(labels);
    }

    [Fact]
    public void Reports_an_unknown_line_clearly()
    {
        var ex = Assert.Throws<GtfsResolutionException>(() => Resolve(line: "999"));
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void Reports_an_unknown_stop_clearly()
    {
        var ex = Assert.Throws<GtfsResolutionException>(() => Resolve(stop: "Няма такава спирка"));
        Assert.Contains("Няма такава спирка", ex.Message);
    }

    [Fact]
    public void Reports_a_line_that_does_not_serve_the_stop()
    {
        // Line 102 exists in the fixture and has trips, but never calls at Воденичарски механи.
        var ex = Assert.Throws<GtfsResolutionException>(() => Resolve(line: "102"));
        Assert.Contains("Воденичарски механи", ex.Message);
    }
}
