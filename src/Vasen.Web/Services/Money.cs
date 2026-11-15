using System.Globalization;
namespace Vasen.Web.Services;
public static class Money
{
    public static string Format(long ore) => (ore / 100m).ToString("N2", CultureInfo.GetCultureInfo("sv-SE")) + " kr";
    public static string Input(long ore) => (ore / 100m).ToString("0.00", CultureInfo.GetCultureInfo("sv-SE"));
    public static bool TryParse(string? text, out long ore)
        => TryRead(text, false, false, out ore);
    public static bool TryParseNonNegative(string? text, out long ore)
        => TryRead(text, true, false, out ore);
    public static bool TryParseSigned(string? text, out long ore)
        => TryRead(text, true, true, out ore);
    private static bool TryRead(string? text, bool allowZero, bool allowNegative, out long ore)
    {
        ore = 0;
        var cleaned = text?.Trim().Replace(" ", "").Replace("\u00a0", "").Replace(",", ".");
        var style = NumberStyles.AllowDecimalPoint | (allowNegative ? NumberStyles.AllowLeadingSign : NumberStyles.None);
        if (!decimal.TryParse(cleaned, style, CultureInfo.InvariantCulture, out var value)
            || (!allowZero && value == 0) || (!allowNegative && value < 0)
            || Math.Abs(value) > 100_000_000 || decimal.Round(value, 2) != value) return false;
        ore = (long)(value * 100);
        return true;
    }
}
