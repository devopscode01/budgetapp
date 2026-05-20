using System.Globalization;
using System.Text.RegularExpressions;
using BudgetApp.Models;

namespace BudgetApp.Services;

public readonly record struct RawStatementLine(DateOnly Date, string Description, decimal SignedAmount);

/// <summary>Heuristic parsers for statement PDF text. Layouts change; tune regexes against your exports.</summary>
public static class StatementLineParser
{
    /// <summary>Chase card lines often look like: MM/DD MERCHANT   12.34</summary>
    private static readonly Regex ChaseMdY = new(
        @"^\s*(?<m>\d{1,2})/(?<d>\d{1,2})\s+(?<desc>.+?)\s+(?<amt>-?\$?\s*\d{1,3}(?:,\d{3})*\.\d{2})\s*$",
        RegexOptions.Compiled);

    /// <summary>Bank of America: MM/DD/YYYY DESCRIPTION  -1,234.56</summary>
    private static readonly Regex BoaFullDate = new(
        @"^\s*(?<mo>\d{1,2})/(?<da>\d{1,2})/(?<y>\d{2,4})\s+(?<desc>.+?)\s+(?<amt>-?\$?\s*\d{1,3}(?:,\d{3})*\.\d{2})\s*$",
        RegexOptions.Compiled);

    /// <summary>Chase PDFs with 2+ spaces between date and description (tight table extraction).</summary>
    private static readonly Regex ChaseTxnBoundary = new(@"(?=\d{1,2}/\d{1,2}\s{2,})", RegexOptions.Compiled);

    /// <summary>Fallback: same split but allows a single space — handles PDFs that extract with minimal spacing.</summary>
    private static readonly Regex ChaseTxnBoundarySingleSpace = new(@"(?=\d{1,2}/\d{1,2}\s+)", RegexOptions.Compiled);

    /// <summary>BofA checking: <c>03/19/26Zelle...</c> (two-digit year glued to description). Amounts often end <c>.0003/19/26</c> with no space.</summary>
    private static readonly Regex BoaGluedDateBoundary = new(
        @"(?=\d{1,2}/\d{1,2}/\d{2}(?=[A-Za-z0-9\*#\(\-]))",
        RegexOptions.Compiled);

    private static readonly Regex BoaGluedLead = new(
        @"^(?<mo>\d{1,2})/(?<da>\d{1,2})/(?<y>\d{2})(?<rest>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex ChaseBlobLead = new(
        @"^(?<m>\d{1,2})/(?<d>\d{1,2})\s+(?<rest>.+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Amount at end of a transaction blob (allows <c>...CHARGE337.56</c> with no space).
    /// Also handles Chase "PURCHASES AND REDEMPTIONS" format where reward points follow the amount:
    /// <c>AMAZON.COM AMZN.COM/BILLWA 75.36 7,536</c> — the trailing integer is reward points, not an amount.
    /// </summary>
    private static readonly Regex TrailingSignedAmount = new(
        @"(?<amt>-?\d{1,3}(?:,\d{3})*\.\d{2})(?:\s+[\d,]+)?\s*$",
        RegexOptions.Compiled);

    public static IReadOnlyList<RawStatementLine> Parse(StatementSource source, string fullText, int defaultYear)
    {
        var lines = fullText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return source switch
        {
            StatementSource.ChaseCredit => ParseChase(fullText, lines, defaultYear),
            StatementSource.BankOfAmerica => ParseBofA(fullText, lines, defaultYear),
            _ => ParseGeneric(fullText, lines, defaultYear)
        };
    }

    private static List<RawStatementLine> ParseChase(string fullText, IReadOnlyList<string> lines, int defaultYear)
    {
        // When source is confirmed Chase, skip the text-trigger gate so the blob parser always runs.
        var fromBlob = ParseChaseFromBlob(fullText, defaultYear, skipGate: true);
        if (fromBlob.Count > 0)
            return fromBlob;

        var result = new List<RawStatementLine>();
        foreach (var line in lines)
        {
            if (line.Length < 10) continue;
            var m = ChaseMdY.Match(line);
            if (!m.Success) continue;
            if (!TryParseAmount(m.Groups["amt"].Value, out var amt)) continue;
            var month = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            var desc = NormalizeDesc(m.Groups["desc"].Value);
            if (desc.Length < 3) continue;
            if (!TryMakeDate(defaultYear, month, day, out var date)) continue;
            result.Add(new RawStatementLine(date, desc, amt));
        }

        return result;
    }

    /// <summary>Chase account activity is often one continuous string; split on date boundaries.</summary>
    /// <param name="skipGate">When <c>true</c> the trigger-phrase check is skipped (use when source is confirmed Chase from filename).</param>
    private static List<RawStatementLine> ParseChaseFromBlob(string fullText, int defaultYear, bool skipGate = false)
    {
        var result = new List<RawStatementLine>();
        if (!skipGate &&
            !fullText.Contains("ACCOUNT ACTIVITY", StringComparison.OrdinalIgnoreCase) &&
            !fullText.Contains("Date ofTransaction", StringComparison.OrdinalIgnoreCase) &&
            !fullText.Contains("Date of Transaction", StringComparison.OrdinalIgnoreCase) &&
            !fullText.Contains("Transaction Date", StringComparison.OrdinalIgnoreCase) &&
            !fullText.Contains("PURCHASES AND ADJUSTMENTS", StringComparison.OrdinalIgnoreCase) &&
            !fullText.Contains("PAYMENT, CREDITS", StringComparison.OrdinalIgnoreCase))
            return result;

        var parts = ChaseTxnBoundary.Split(fullText);
        var txns = ExtractChasePartsToLines(parts, defaultYear);

        // If the tight-spacing split found nothing, retry with a single-space boundary.
        // Modern Chase PDFs sometimes extract with only one space between the date and description.
        if (txns.Count == 0)
        {
            var partsSingle = ChaseTxnBoundarySingleSpace.Split(fullText);
            txns = ExtractChasePartsToLines(partsSingle, defaultYear);
        }

        return txns;
    }

    private static List<RawStatementLine> ExtractChasePartsToLines(string[] parts, int defaultYear)
    {
        var result = new List<RawStatementLine>();
        foreach (var part in parts)
        {
            var seg = part.Trim();
            if (seg.Length < 12) continue;
            var lead = ChaseBlobLead.Match(seg);
            if (!lead.Success) continue;
            if (!int.TryParse(lead.Groups["m"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month))
                continue;
            if (!int.TryParse(lead.Groups["d"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
                continue;
            var rest = lead.Groups["rest"].Value;
            if (!TrySplitDescAndTrailingAmount(rest, out var desc, out var amt)) continue;
            desc = NormalizeDesc(desc);
            if (desc.Length < 3 || ShouldSkipChaseDescription(desc)) continue;
            if (!TryMakeDate(defaultYear, month, day, out var date)) continue;
            result.Add(new RawStatementLine(date, desc, amt));
        }
        return result;
    }

    private static bool ShouldSkipChaseDescription(string desc)
    {
        var u = desc.ToUpperInvariant();
        if (u.StartsWith("TOTAL ", StringComparison.Ordinal)) return true;
        if (u.Contains("YEAR-TO-DATE", StringComparison.Ordinal)) return true;
        if (u.StartsWith("STATEMENT DATE", StringComparison.Ordinal)) return true;
        if (u.StartsWith("PAYMENTS AND OTHER", StringComparison.Ordinal)) return true;
        // Chase summary/balance lines that can look like transactions when dates appear nearby.
        if (u.Contains("NEW BALANCE", StringComparison.Ordinal)) return true;
        if (u.Contains("MINIMUM PAYMENT", StringComparison.Ordinal)) return true;
        if (u.Contains("CREDIT ACCESS LINE", StringComparison.Ordinal)) return true;
        if (u.Contains("AVAILABLE CREDIT", StringComparison.Ordinal)) return true;
        if (u.StartsWith("BALANCE SUBJECT", StringComparison.Ordinal)) return true;
        // Descriptions that are clearly parser artifacts (e.g. "/26 New Balance $").
        if (desc.StartsWith("/", StringComparison.Ordinal)) return true;
        return false;
    }

    private static List<RawStatementLine> ParseBofA(string fullText, IReadOnlyList<string> lines, int defaultYear)
    {
        // When source is confirmed BofA, skip the text-trigger gate so the glued-date parser always runs.
        var fromGlued = ParseBofAFromGluedDates(fullText, defaultYear, skipGate: true);
        if (fromGlued.Count > 0)
            return fromGlued;

        var result = new List<RawStatementLine>();
        foreach (var line in lines)
        {
            if (line.Length < 12) continue;
            var m = BoaFullDate.Match(line);
            if (m.Success)
            {
                if (!TryParseAmount(m.Groups["amt"].Value, out var amt)) continue;
                var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
                if (y < 100) y += 2000;
                var mo = int.Parse(m.Groups["mo"].Value, CultureInfo.InvariantCulture);
                var da = int.Parse(m.Groups["da"].Value, CultureInfo.InvariantCulture);
                if (!TryMakeDate(y, mo, da, out var date)) continue;
                var desc = NormalizeDesc(m.Groups["desc"].Value);
                if (desc.Length < 3) continue;
                result.Add(new RawStatementLine(date, desc, amt));
                continue;
            }

            // Fallback: MM/DD without year (use defaultYear)
            var m2 = ChaseMdY.Match(line);
            if (!m2.Success) continue;
            if (!TryParseAmount(m2.Groups["amt"].Value, out var amt2)) continue;
            var month = int.Parse(m2.Groups["m"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(m2.Groups["d"].Value, CultureInfo.InvariantCulture);
            var desc2 = NormalizeDesc(m2.Groups["desc"].Value);
            if (desc2.Length < 3) continue;
            if (!TryMakeDate(defaultYear, month, day, out var date2)) continue;
            result.Add(new RawStatementLine(date2, desc2, amt2));
        }

        return result;
    }

    /// <param name="skipGate">When <c>true</c> the "Bank of America" text check is skipped (use when source is confirmed BofA from filename).</param>
    private static List<RawStatementLine> ParseBofAFromGluedDates(string fullText, int defaultYear, bool skipGate = false)
    {
        var result = new List<RawStatementLine>();
        if (!skipGate &&
            !fullText.Contains("Bank of America", StringComparison.OrdinalIgnoreCase) &&
            !fullText.Contains("bankofamerica.com", StringComparison.OrdinalIgnoreCase))
            return result;

        var parts = BoaGluedDateBoundary.Split(fullText);
        foreach (var part in parts)
        {
            var seg = part.Trim();
            if (seg.Length < 12) continue;
            var m = BoaGluedLead.Match(seg);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups["mo"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mo))
                continue;
            if (!int.TryParse(m.Groups["da"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var da))
                continue;
            if (!int.TryParse(m.Groups["y"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y2))
                continue;
            var y = y2 < 100 ? y2 + 2000 : y2;
            var rest = m.Groups["rest"].Value;
            if (!TrySplitDescAndTrailingAmount(rest, out var desc, out var amt)) continue;
            desc = NormalizeDesc(desc);
            if (desc.Length < 3 || ShouldSkipBofADescription(desc)) continue;
            if (!TryMakeDate(y, mo, da, out var date)) continue;
            result.Add(new RawStatementLine(date, desc, amt));
        }

        return result;
    }

    private static bool ShouldSkipBofADescription(string desc)
    {
        var u = desc.ToUpperInvariant();
        if (u.StartsWith("TOTAL ", StringComparison.Ordinal)) return true;
        if (u.StartsWith("BEGINNING BALANCE", StringComparison.Ordinal)) return true;
        if (u.StartsWith("ENDING BALANCE", StringComparison.Ordinal)) return true;
        if (u.Contains("DATEDESCRIPTIONAMOUNT", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool TrySplitDescAndTrailingAmount(string rest, out string desc, out decimal amt)
    {
        desc = "";
        amt = 0;
        var m = TrailingSignedAmount.Match(rest);
        if (!m.Success) return false;
        if (!TryParseAmount(m.Groups["amt"].Value, out amt)) return false;
        desc = rest[..m.Index].TrimEnd();
        return true;
    }

    private static List<RawStatementLine> ParseGeneric(string fullText, IReadOnlyList<string> lines, int defaultYear)
    {
        // Use text gates for the generic path since we have no filename confirmation.
        var chase = ParseChaseFromBlob(fullText, defaultYear, skipGate: false);
        if (chase.Count > 0)
            return chase;
        var boa = ParseBofAFromGluedDates(fullText, defaultYear, skipGate: false);
        if (boa.Count > 0)
            return boa;

        var result = new List<RawStatementLine>();
        foreach (var line in lines)
        {
            if (line.Length < 12) continue;
            var m = BoaFullDate.Match(line);
            if (m.Success)
            {
                if (!TryParseAmount(m.Groups["amt"].Value, out var amt)) continue;
                var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
                if (y < 100) y += 2000;
                var mo = int.Parse(m.Groups["mo"].Value, CultureInfo.InvariantCulture);
                var da = int.Parse(m.Groups["da"].Value, CultureInfo.InvariantCulture);
                if (!TryMakeDate(y, mo, da, out var date)) continue;
                var desc = NormalizeDesc(m.Groups["desc"].Value);
                if (desc.Length < 3) continue;
                result.Add(new RawStatementLine(date, desc, amt));
                continue;
            }

            var m2 = ChaseMdY.Match(line);
            if (!m2.Success) continue;
            if (!TryParseAmount(m2.Groups["amt"].Value, out var amt2)) continue;
            var month = int.Parse(m2.Groups["m"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(m2.Groups["d"].Value, CultureInfo.InvariantCulture);
            var desc2 = NormalizeDesc(m2.Groups["desc"].Value);
            if (desc2.Length < 3) continue;
            if (!TryMakeDate(defaultYear, month, day, out var date2)) continue;
            result.Add(new RawStatementLine(date2, desc2, amt2));
        }

        return result;
    }

    private static bool TryParseAmount(string raw, out decimal value)
    {
        value = 0;
        raw = raw.Trim();
        if (raw.Length == 0) return false;
        raw = raw.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        return decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeDesc(string s)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static bool TryMakeDate(int year, int month, int day, out DateOnly date)
    {
        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch
        {
            date = default;
            return false;
        }
    }
}
