using System.Globalization;

namespace TripUpdates.Gtfs;

public static class GtfsTime
{
    /// <summary>
    /// Parses a GTFS "HH:MM:SS" time as an offset from the start of the service day. Hours run past
    /// 24 for trips that cross midnight — "24:40:00" is 00:40 the next morning — so TimeSpan.Parse
    /// cannot be used: it reads a leading 24 as a day count and overflows.
    /// </summary>
    public static TimeSpan? Parse(string value)
    {
        var span = value.AsSpan().Trim();
        Span<Range> parts = stackalloc Range[4];
        if (span.Split(parts, ':') != 3) return null;

        if (!int.TryParse(span[parts[0]], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(span[parts[1]], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(span[parts[2]], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return null;

        if (minutes > 59 || seconds > 59) return null;

        return new TimeSpan(hours, minutes, seconds);
    }
}
