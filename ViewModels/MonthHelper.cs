using System.Globalization;

namespace BudgetApp.ViewModels;

public static class MonthHelper
{
    public static DateOnly ParseMonth(string? ym)
    {
        if (!string.IsNullOrWhiteSpace(ym) && ym.Length >= 7)
        {
            var s = ym.Substring(0, 7) + "-01";
            if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return new DateOnly(d.Year, d.Month, 1);
        }

        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 1);
    }

    public static string FormatYm(DateOnly month) => month.ToString("yyyy-MM");
}
