namespace TripUpdates.Tests;

internal static class Fixtures
{
    public static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static Stream StaticFeed() => File.OpenRead(Path("gtfs-static-a63.zip"));
    public static Stream TripUpdates() => File.OpenRead(Path("trip-updates-a63.pb"));

    /// <summary>The header timestamp of the captured trip-updates fixture.</summary>
    public static readonly DateTimeOffset FeedCapturedAt = DateTimeOffset.FromUnixTimeSeconds(1788709367);
}
