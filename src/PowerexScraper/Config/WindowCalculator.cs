namespace PowerexScraper.Config;

public sealed record ScrapeWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc, DateOnly DataDate);

/// <summary>Turns a declarative WindowSpec into a UTC range covering whole local calendar days.
/// The only place DST math lives: local midnights are converted with TimeZoneInfo, so
/// 23/25-hour DST days come out correct by construction.</summary>
public sealed class WindowCalculator(IClock clock)
{
    public ScrapeWindow Calculate(WindowSpec spec, DateOnly? dateOverride = null)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(spec.AnchorTimeZone);
        var anchor = dateOverride
                     ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, tz).DateTime);
        var startDay = anchor.AddDays(spec.StartOffsetDays);
        var endDay = startDay.AddDays(spec.DurationDays);
        return new ScrapeWindow(LocalMidnightUtc(startDay, tz), LocalMidnightUtc(endDay, tz), startDay);
    }

    private static DateTimeOffset LocalMidnightUtc(DateOnly day, TimeZoneInfo tz)
    {
        var localMidnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz), TimeSpan.Zero);
    }
}
