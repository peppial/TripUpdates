using TripUpdates.Arrivals;
using TripUpdates.Configuration;
using TripUpdates.Gtfs;

namespace TripUpdates.Tests;

/// <summary>
/// Sofia's realtime feed only carries trips that are already running, so a mountain route with
/// hour-long gaps has no prediction most of the day. The printed timetable has to fill that in,
/// or the board says "няма курсове" while a bus is an hour out.
/// </summary>
public class ScheduledFallbackTests
{
    private static readonly TimeZoneInfo Sofia = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia");

    // Monday 2026-09-07, 17:19 Sofia time — the moment the bug was reported.
    private static readonly DateTimeOffset Now =
        new DateTimeOffset(2026, 9, 7, 17, 19, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    private static StaticCatalog CatalogWithTimetable(params string[] departures) =>
        new("66", "A63", "Воденичарски механи",
        [
            new DirectionBinding("към Алеко", ["A1471"], ["ХИЖА АЛЕКО"])
            {
                Timetable = [.. departures.Select(d => new ScheduledDeparture($"trip-{d}", "weekday", GtfsTime.Parse(d)!.Value))],
            },
        ])
        {
            TimeZone = Sofia,
            ServiceDates = new Dictionary<DateOnly, IReadOnlySet<string>>
            {
                [new DateOnly(2026, 9, 7)] = new HashSet<string> { "weekday" },
                [new DateOnly(2026, 9, 8)] = new HashSet<string> { "weekday" },
            },
        };

    private static ArrivalsService Service(TimeSpan? horizon = null) =>
        new(new FeedOptions { Horizon = horizon ?? TimeSpan.FromHours(2) });

    [Fact]
    public void Falls_back_to_the_timetable_when_realtime_has_no_prediction()
    {
        var catalog = CatalogWithTimetable("16:11:00", "17:11:00", "18:16:00", "20:16:00");

        var aleko = Service(TimeSpan.FromHours(4)).Build(catalog, FeedState.Initial, Now).Directions[0];

        Assert.True(aleko.HasArrival);
        Assert.True(aleko.Scheduled);
        Assert.Equal(57, aleko.Minutes);        // 17:19 -> 18:16
        Assert.Equal(177, aleko.ThenMinutes);   // 17:19 -> 20:16
        Assert.Equal("66 към Алеко по разписание след 57 минути, следващият след 177 минути", aleko.Message);
    }

    [Fact]
    public void Prefers_a_live_prediction_over_the_timetable()
    {
        var catalog = CatalogWithTimetable("18:16:00");
        var byStop = new Dictionary<string, IReadOnlyList<DateTimeOffset>> { ["A1471"] = [Now.AddMinutes(4)] };
        var state = new FeedState(new RealtimeSnapshot(byStop, Now, Now), null);

        var aleko = Service().Build(catalog, state, Now).Directions[0];

        Assert.False(aleko.Scheduled);
        Assert.Equal(4, aleko.Minutes);

        // The live bus is first; the printed 18:16 tops up the second slot behind it.
        Assert.True(aleko.ThenScheduled);
        Assert.Equal(57, aleko.ThenMinutes);
        Assert.Equal(
            "66 към Алеко ще дойде след 4 минути, следващият по разписание след 57 минути",
            aleko.Message);
    }

    [Fact]
    public void Counts_a_departure_shared_by_two_services_once()
    {
        // Feeds routinely split one timetable across several service ids. Without de-duplication
        // the card reads "57, 57" — two buses where there is one.
        var catalog = new StaticCatalog("66", "A63", "Воденичарски механи",
        [
            new DirectionBinding("към Алеко", ["A1471"], ["ХИЖА АЛЕКО"])
            {
                Timetable =
                [
                    new ScheduledDeparture("t-weekday", "weekday", new TimeSpan(18, 16, 0)),
                    new ScheduledDeparture("t-school", "school", new TimeSpan(18, 16, 0)),
                    new ScheduledDeparture("t-weekday-late", "weekday", new TimeSpan(20, 16, 0)),
                ],
            },
        ])
        {
            TimeZone = Sofia,
            ServiceDates = new Dictionary<DateOnly, IReadOnlySet<string>>
            {
                [new DateOnly(2026, 9, 7)] = new HashSet<string> { "weekday", "school" },
            },
        };

        var aleko = Service(TimeSpan.FromHours(4)).Build(catalog, FeedState.Initial, Now).Directions[0];

        Assert.Equal(57, aleko.Minutes);
        Assert.Equal(177, aleko.ThenMinutes);
    }

    [Fact]
    public void Tops_up_the_second_slot_from_the_timetable_when_realtime_has_only_one_bus()
    {
        // What the official board does: 21 minutes tracked, 49 from the printed timetable.
        var catalog = CatalogWithTimetable("17:40:00", "18:08:00");
        var byStop = new Dictionary<string, IReadOnlyList<DateTimeOffset>> { ["A1471"] = [Now.AddMinutes(21)] };
        var state = new FeedState(new RealtimeSnapshot(byStop, Now, Now)
        {
            CoveredTripIds = new HashSet<string> { "trip-17:40:00" },
        }, null);

        var aleko = Service().Build(catalog, state, Now).Directions[0];

        Assert.Equal(21, aleko.Minutes);
        Assert.False(aleko.Scheduled);
        Assert.Equal(49, aleko.ThenMinutes);
        Assert.True(aleko.ThenScheduled);
    }

    [Fact]
    public void Shows_a_printed_bus_that_is_due_before_the_only_one_realtime_knows_about()
    {
        // Observed against the live feed: at 17:47 realtime carried only the 18:26 bus, because a
        // trip is published once it starts running. The 17:56 was still just a timetable row — and
        // it is the bus the rider is actually waiting for. The official board showed "10, 38".
        var catalog = CatalogWithTimetable("17:56:00", "18:26:00", "19:26:00");
        var byStop = new Dictionary<string, IReadOnlyList<DateTimeOffset>>
        {
            ["A1471"] = [Now.AddMinutes(39)],   // 17:19 + 39 = the 17:58 arrival of the 18:26 trip
        };
        var state = new FeedState(new RealtimeSnapshot(byStop, Now, Now)
        {
            CoveredTripIds = new HashSet<string> { "trip-18:26:00" },
        }, null);

        var aleko = Service().Build(catalog, state, Now).Directions[0];

        Assert.Equal(37, aleko.Minutes);        // 17:56 printed, still ahead of the tracked bus
        Assert.True(aleko.Scheduled);
        Assert.Equal(39, aleko.ThenMinutes);    // the tracked 18:26
        Assert.False(aleko.ThenScheduled);
    }

    [Fact]
    public void Does_not_offer_a_printed_departure_for_a_bus_realtime_is_already_tracking()
    {
        // The 18:08 bus is running two minutes early. Without excluding its trip it would appear
        // twice: once as the tracked 47 minutes, once as its printed 49.
        var catalog = CatalogWithTimetable("18:08:00", "19:30:00");
        var byStop = new Dictionary<string, IReadOnlyList<DateTimeOffset>> { ["A1471"] = [Now.AddMinutes(47)] };
        var state = new FeedState(new RealtimeSnapshot(byStop, Now, Now)
        {
            CoveredTripIds = new HashSet<string> { "trip-18:08:00" },
        }, null);

        var aleko = Service(TimeSpan.FromHours(4)).Build(catalog, state, Now).Directions[0];

        Assert.Equal(47, aleko.Minutes);
        Assert.Equal(131, aleko.ThenMinutes);   // 19:30, not the 18:08 it is already tracking
    }

    [Fact]
    public void Collapses_a_tracked_bus_and_its_printed_twin_onto_one_row()
    {
        // If the two feeds ever disagree about a trip id, the printed departure is not excluded.
        // It must still not surface as a second bus one minute behind the tracked one.
        var catalog = CatalogWithTimetable("17:40:00", "18:08:00");
        var byStop = new Dictionary<string, IReadOnlyList<DateTimeOffset>> { ["A1471"] = [Now.AddMinutes(21)] };
        var state = new FeedState(new RealtimeSnapshot(byStop, Now, Now), null);   // nothing excluded

        var aleko = Service().Build(catalog, state, Now).Directions[0];

        Assert.Equal(21, aleko.Minutes);
        Assert.False(aleko.Scheduled);          // the live reading wins the tie
        Assert.Equal(49, aleko.ThenMinutes);    // and the next row is the following bus, not a twin
    }

    [Fact]
    public void Hides_a_bus_further_out_than_the_horizon()
    {
        // Last bus up the mountain at 20:16, then nothing until 07:46 — a "801 минути" second
        // number is noise, not information.
        var catalog = CatalogWithTimetable("19:00:00", "23:30:00");

        var aleko = Service(TimeSpan.FromMinutes(120)).Build(catalog, FeedState.Initial, Now).Directions[0];

        Assert.Equal(101, aleko.Minutes);       // 17:19 -> 19:00
        Assert.Null(aleko.ThenMinutes);         // 23:30 is 371 minutes out
        Assert.Equal("66 към Алеко по разписание след 101 минути", aleko.Message);
    }

    [Fact]
    public void Says_nothing_is_coming_when_even_the_first_bus_is_past_the_horizon()
    {
        var catalog = CatalogWithTimetable("23:30:00");

        var aleko = Service(TimeSpan.FromMinutes(120)).Build(catalog, FeedState.Initial, Now).Directions[0];

        Assert.False(aleko.HasArrival);
        Assert.Equal("66 към Алеко — няма предстоящи курсове", aleko.Message);
    }

    [Fact]
    public void Keeps_a_bus_sitting_exactly_on_the_horizon()
    {
        var catalog = CatalogWithTimetable("19:19:00");   // 17:19 + 120

        var aleko = Service(TimeSpan.FromMinutes(120)).Build(catalog, FeedState.Initial, Now).Directions[0];

        Assert.Equal(120, aleko.Minutes);
    }

    [Fact]
    public void Applies_the_horizon_to_live_predictions_too()
    {
        var catalog = CatalogWithTimetable("18:16:00");
        var byStop = new Dictionary<string, IReadOnlyList<DateTimeOffset>> { ["A1471"] = [Now.AddMinutes(200)] };
        var state = new FeedState(new RealtimeSnapshot(byStop, Now, Now), null);

        var aleko = Service(TimeSpan.FromMinutes(120)).Build(catalog, state, Now).Directions[0];

        Assert.Equal(57, aleko.Minutes);        // the printed 18:16, not the far-off tracked bus
        Assert.Null(aleko.ThenMinutes);
    }

    [Fact]
    public void Says_nothing_is_coming_once_the_timetable_is_done_for_the_day()
    {
        var catalog = CatalogWithTimetable("06:11:00", "07:11:00");
        var lateEvening = new DateTimeOffset(2026, 9, 8, 23, 30, 0, TimeSpan.FromHours(3)).ToUniversalTime();

        var aleko = Service().Build(catalog, FeedState.Initial, lateEvening).Directions[0];

        Assert.False(aleko.HasArrival);
        Assert.Equal("66 към Алеко — няма предстоящи курсове", aleko.Message);
    }

    [Fact]
    public void Only_counts_departures_whose_service_runs_that_day()
    {
        var catalog = new StaticCatalog("66", "A63", "Воденичарски механи",
        [
            new DirectionBinding("към Алеко", ["A1471"], ["ХИЖА АЛЕКО"])
            {
                Timetable =
                [
                    new ScheduledDeparture("t-weekend", "weekend", new TimeSpan(17, 30, 0)),
                    new ScheduledDeparture("t-weekday", "weekday", new TimeSpan(18, 16, 0)),
                ],
            },
        ])
        {
            TimeZone = Sofia,
            ServiceDates = new Dictionary<DateOnly, IReadOnlySet<string>>
            {
                [new DateOnly(2026, 9, 7)] = new HashSet<string> { "weekday" },
            },
        };

        var aleko = Service().Build(catalog, FeedState.Initial, Now).Directions[0];

        Assert.Equal(57, aleko.Minutes);       // 17:30 belongs to the weekend service, so it is skipped
        Assert.Null(aleko.ThenMinutes);
    }

    [Fact]
    public void Handles_a_departure_written_past_midnight_as_belonging_to_the_previous_day()
    {
        // GTFS allows "24:40:00" for a bus that leaves at 00:40 on the following calendar day.
        var catalog = CatalogWithTimetable("24:40:00");
        var justBeforeMidnight = new DateTimeOffset(2026, 9, 7, 23, 55, 0, TimeSpan.FromHours(3)).ToUniversalTime();

        var aleko = Service().Build(catalog, FeedState.Initial, justBeforeMidnight).Directions[0];

        Assert.True(aleko.HasArrival);
        Assert.Equal(45, aleko.Minutes);
    }
}
