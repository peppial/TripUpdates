namespace TripUpdates.Arrivals;

public static class BulgarianText
{
    /// <summary>
    /// "66 към Алеко ще дойде след 5 минути", and its edge cases. When a second bus is known it
    /// is appended, so the card's aria-label announces everything the card shows.
    /// </summary>
    public static string Arrival(
        string line, string label, int minutes,
        int? thenMinutes = null, bool scheduled = false, bool thenScheduled = false)
    {
        var first = (scheduled, minutes) switch
        {
            (false, <= 0) => $"{line} {label} пристига сега",
            (false, 1) => $"{line} {label} ще дойде след 1 минута",
            (false, _) => $"{line} {label} ще дойде след {minutes} минути",
            (true, <= 0) => $"{line} {label} по разписание сега",
            (true, 1) => $"{line} {label} по разписание след 1 минута",
            (true, _) => $"{line} {label} по разписание след {minutes} минути",
        };

        // Only worth saying on the second bus when the first was live: if both come from the
        // timetable the sentence already opened with "по разписание".
        var by = thenScheduled && !scheduled ? " по разписание" : "";
        var then = (thenMinutes, by) switch
        {
            (null, _) => "",
            ( <= 0, "") => ", следващият също пристига сега",
            ( <= 0, _) => $", следващият{by} пристига сега",
            (1, _) => $", следващият{by} след 1 минута",
            _ => $", следващият{by} след {thenMinutes} минути",
        };

        return first + then;
    }

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
