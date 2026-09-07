using System.IO.Compression;
using System.Text;
using TripUpdates.Configuration;
using TripUpdates.Gtfs;

namespace TripUpdates.Tests;

/// <summary>
/// The Sofia feed has no calendar.txt — every service day is spelled out in calendar_dates.txt
/// instead, ~950 services per date. These build a miniature feed rather than ship another zip.
/// </summary>
public class GtfsCalendarTests
{
    private static Stream Feed(string calendarDates, string agencyTimezone = "Europe/Sofia")
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string body)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(body);
            }

            Add("agency.txt", $"agency_id,agency_name,agency_url,agency_timezone\n1,ЦГМ,https://x,{agencyTimezone}\n");
            Add("routes.txt", "route_id,route_short_name,route_type\nA63,66,3\n");
            Add("stops.txt", "stop_id,stop_name\nA1471,ВОДЕНИЧАРСКИ МЕХАНИ-ПО ЖЕЛАНИЕ\n");
            Add("trips.txt",
                "trip_id,route_id,service_id,trip_headsign\n" +
                "t-weekday,A63,weekday,ХИЖА АЛЕКО\n" +
                "t-weekend,A63,weekend,ХИЖА АЛЕКО\n");
            Add("stop_times.txt",
                "trip_id,arrival_time,departure_time,stop_id,stop_sequence\n" +
                "t-weekday,18:16:00,18:16:00,A1471,1\n" +
                "t-weekend,10:30:00,10:30:00,A1471,1\n");
            if (calendarDates.Length > 0) Add("calendar_dates.txt", calendarDates);
        }
        buffer.Position = 0;
        return buffer;
    }

    private static readonly List<DirectionOptions> Aleko =
        [new() { Label = "към Алеко", Headsigns = ["ХИЖА АЛЕКО"] }];

    private static StaticCatalog Resolve(string calendarDates, string tz = "Europe/Sofia")
    {
        using var zip = Feed(calendarDates, tz);
        return GtfsStaticParser.Resolve(zip, "66", "Воденичарски механи", Aleko);
    }

    [Fact]
    public void Reads_service_days_from_calendar_dates()
    {
        var catalog = Resolve(
            "service_id,date,exception_type\n" +
            "weekday,20260907,1\n" +
            "weekend,20260912,1\n");

        Assert.Equal(
            ["weekday"],
            catalog.ServiceDates[new DateOnly(2026, 9, 7)].Order().ToArray());
        Assert.Equal(
            ["weekend"],
            catalog.ServiceDates[new DateOnly(2026, 9, 12)].Order().ToArray());
    }

    [Fact]
    public void Honours_a_removal_exception()
    {
        var catalog = Resolve(
            "service_id,date,exception_type\n" +
            "weekday,20260907,1\n" +
            "weekday,20260907,2\n");

        Assert.Empty(catalog.ServiceDates.GetValueOrDefault(new DateOnly(2026, 9, 7), new HashSet<string>()));
    }

    [Fact]
    public void Offers_only_the_departure_whose_service_runs_that_day()
    {
        var catalog = Resolve(
            "service_id,date,exception_type\n" +
            "weekday,20260907,1\n" +
            "weekend,20260906,1\n");

        // Monday 07:00 Sofia — the weekday 18:16 is next; the weekend 10:30 belongs to Sunday.
        var monday = new DateTimeOffset(2026, 9, 7, 7, 0, 0, TimeSpan.FromHours(3));
        var next = catalog.ScheduledAfter(catalog.Directions[0], monday).First();

        Assert.Equal(new DateTimeOffset(2026, 9, 7, 18, 16, 0, TimeSpan.FromHours(3)), next);
    }

    [Fact]
    public void Reads_the_agency_timezone_so_times_are_not_off_by_the_utc_offset()
    {
        var catalog = Resolve("service_id,date,exception_type\nweekday,20260907,1\n");

        Assert.Equal("Europe/Sofia", catalog.TimeZone.Id);

        // 18:16 Sofia in September is 15:16 UTC.
        var next = catalog.ScheduledAfter(
            catalog.Directions[0], new DateTimeOffset(2026, 9, 7, 6, 0, 0, TimeSpan.Zero)).First();
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 15, 16, 0, TimeSpan.Zero), next.ToUniversalTime());
    }
}
