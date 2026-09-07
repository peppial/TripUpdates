namespace TripUpdates.Arrivals;

public static class BulgarianText
{
    /// <summary>"66 към Алеко ще дойде след 5 минути", and its edge cases.</summary>
    public static string Arrival(string line, string label, int minutes) => minutes switch
    {
        <= 0 => $"{line} {label} пристига сега",
        1 => $"{line} {label} ще дойде след 1 минута",
        _ => $"{line} {label} ще дойде след {minutes} минути",
    };

    public static string NoArrival(string line, string label) =>
        $"{line} {label} — няма предстоящи курсове";

    public static string Ago(int seconds) => seconds switch
    {
        <= 1 => "обновено сега",
        < 60 => $"обновено преди {seconds} сек",
        < 120 => "обновено преди 1 мин",
        _ => $"обновено преди {seconds / 60} мин",
    };
}
