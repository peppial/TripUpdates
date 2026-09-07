using TripUpdates.Arrivals;

namespace TripUpdates.Tests;

public class BulgarianTextTests
{
    [Theory]
    [InlineData(0, "66 към Алеко пристига сега")]
    [InlineData(1, "66 към Алеко ще дойде след 1 минута")]
    [InlineData(2, "66 към Алеко ще дойде след 2 минути")]
    [InlineData(43, "66 към Алеко ще дойде след 43 минути")]
    public void Uses_the_right_plural_form(int minutes, string expected)
    {
        Assert.Equal(expected, BulgarianText.Arrival("66", "към Алеко", minutes));
    }

    [Fact]
    public void Says_arriving_now_rather_than_a_negative_number()
    {
        Assert.Equal("66 към София пристига сега", BulgarianText.Arrival("66", "към София", -3));
    }

    [Fact]
    public void Has_a_message_for_no_upcoming_service()
    {
        Assert.Equal("66 към Алеко — няма предстоящи курсове", BulgarianText.NoArrival("66", "към Алеко"));
    }

    [Theory]
    [InlineData(0, "обновено сега")]
    [InlineData(12, "обновено преди 12 сек")]
    [InlineData(75, "обновено преди 1 мин")]
    [InlineData(200, "обновено преди 3 мин")]
    public void Describes_how_old_the_reading_is(int seconds, string expected)
    {
        Assert.Equal(expected, BulgarianText.Ago(seconds));
    }
}
