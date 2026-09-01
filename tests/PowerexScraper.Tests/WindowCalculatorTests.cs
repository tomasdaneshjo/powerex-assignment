using PowerexScraper.Config;

namespace PowerexScraper.Tests;

public class WindowCalculatorTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }

    private static readonly WindowSpec NextDay = new("Europe/Bratislava", 1, 1);
    private static readonly WindowSpec PrevDay = new("Europe/Bratislava", -1, 1);

    [Fact]
    public void Summer_next_day_window_uses_2200Z_boundaries()
    {
        // 2026-08-31 17:30 CEST = 15:30Z
        var calc = new WindowCalculator(new FixedClock(new DateTimeOffset(2026, 8, 31, 15, 30, 0, TimeSpan.Zero)));
        var w = calc.Calculate(NextDay);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero), w.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 22, 0, 0, TimeSpan.Zero), w.ToUtc);
        Assert.Equal(new DateOnly(2026, 9, 1), w.DataDate);
    }

    [Fact]
    public void Winter_previous_day_window_uses_2300Z_boundaries()
    {
        // 2026-01-15 09:30 CET = 08:30Z
        var calc = new WindowCalculator(new FixedClock(new DateTimeOffset(2026, 1, 15, 8, 30, 0, TimeSpan.Zero)));
        var w = calc.Calculate(PrevDay);
        Assert.Equal(new DateTimeOffset(2026, 1, 13, 23, 0, 0, TimeSpan.Zero), w.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 1, 14, 23, 0, 0, TimeSpan.Zero), w.ToUtc);
        Assert.Equal(new DateOnly(2026, 1, 14), w.DataDate);
    }

    [Fact]
    public void Spring_dst_day_is_23_hours()
    {
        // DST starts 2026-03-29 in EU; that local day = 2026-03-28T23:00Z → 2026-03-29T22:00Z
        var calc = new WindowCalculator(new FixedClock(new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero)));
        var w = calc.Calculate(NextDay);
        Assert.Equal(new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero), w.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 22, 0, 0, TimeSpan.Zero), w.ToUtc);
        Assert.Equal(TimeSpan.FromHours(23), w.ToUtc - w.FromUtc);
    }

    [Fact]
    public void Autumn_dst_day_is_25_hours()
    {
        // DST ends 2026-10-25 in EU; that local day = 2026-10-24T22:00Z → 2026-10-25T23:00Z
        var calc = new WindowCalculator(new FixedClock(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero)));
        var w = calc.Calculate(NextDay);
        Assert.Equal(TimeSpan.FromHours(25), w.ToUtc - w.FromUtc);
        Assert.Equal(new DateOnly(2026, 10, 25), w.DataDate);
    }

    [Fact]
    public void Date_override_replaces_today_as_anchor()
    {
        var calc = new WindowCalculator(new FixedClock(new DateTimeOffset(2026, 8, 31, 15, 30, 0, TimeSpan.Zero)));
        var w = calc.Calculate(PrevDay, dateOverride: new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 6, 14), w.DataDate);
        Assert.Equal(new DateTimeOffset(2026, 6, 13, 22, 0, 0, TimeSpan.Zero), w.FromUtc);
    }

    [Fact]
    public void Year_boundary_rolls_over()
    {
        // 2026-12-31 17:30 CET = 16:30Z
        var calc = new WindowCalculator(new FixedClock(new DateTimeOffset(2026, 12, 31, 16, 30, 0, TimeSpan.Zero)));
        var w = calc.Calculate(NextDay);
        Assert.Equal(new DateOnly(2027, 1, 1), w.DataDate);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero), w.FromUtc);
    }
}
