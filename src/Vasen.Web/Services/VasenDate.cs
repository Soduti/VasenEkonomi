namespace Vasen.Web.Services;

public static class VasenDate
{
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm")).DateTime);
}
