using System.Globalization;

namespace BudgetApp.Services;

/// <summary>Creates optional <c>january_2026</c>-style folders (English month names, lowercase) under the inbox root.</summary>
public static class MonthInboxFolderFactory
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Creates one folder per month for each year in the inclusive range (e.g. 2026–2027 → all months in both years).</summary>
    public static IReadOnlyList<string> EnsureEnglishMonthFolders(string inboxRoot, int startYear, int endYear)
    {
        if (string.IsNullOrWhiteSpace(inboxRoot)) return [];
        Directory.CreateDirectory(inboxRoot);
        if (endYear < startYear) (startYear, endYear) = (endYear, startYear);

        var created = new List<string>();
        for (var y = startYear; y <= endYear; y++)
        {
            for (var m = 1; m <= 12; m++)
            {
                var monthName = English.DateTimeFormat.GetMonthName(m).ToLowerInvariant();
                var folder = $"{monthName}_{y}";
                var full = Path.Combine(inboxRoot, folder);
                Directory.CreateDirectory(full);
                created.Add(folder);
            }
        }

        return created;
    }

    /// <summary>Folder name for a calendar month, e.g. April 2026 → <c>april_2026</c>.</summary>
    public static string MonthFolderName(DateOnly month)
    {
        var name = English.DateTimeFormat.GetMonthName(month.Month).ToLowerInvariant();
        return $"{name}_{month.Year}";
    }
}
