using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BudgetApp.Models;
using BudgetApp.Options;
using Microsoft.Extensions.Options;

namespace BudgetApp.Services;

public sealed class ExpenseClassifier(IOptions<BudgetOptions> options)
{
    private readonly BudgetOptions _opt = options.Value;

    private static readonly (string Pattern, ExpenseCategory Cat)[] Rules =
    [
        (@"(MORTGAGE|MTG PAY|HOME LOAN|ESCROW|LOAN PMT|HOME MTG)", ExpenseCategory.Mortgage),
        (@"(WATER|SEWER|AQUA\s|AQUA-|CITY\s*OF.*WATER|MUSTANG|MUSTANG\s+SUD)", ExpenseCategory.Water),
        (@"(ELECTRIC|UTIL|UTILITY|POWER|ENERGY|PEPCO|DUKE\s|COMED|PGE|SMUD|CON\s*ED|NGRID|COSERV|CO\.?\s*SERV|ATMOS)", ExpenseCategory.Utilities),
        (@"(INSURANCE|GEICO|STATE\s*FARM|ALLSTATE|PROGRESSIVE|USAA|LIBERTY\s*MUTUAL|HUMANA|ANTHEM|CIGNA|AETNA|KAISER)", ExpenseCategory.Insurance),
        (@"(NETFLIX|SPOTIFY|HULU|DISNEY|HBO|APPLE\.COM|GOOGLE\s|OPENAI|CURSOR|GITHUB|DROPBOX|ADOBE|MICROSOFT\s*365|AMAZON\s*PRIME|YOUTUBE)", ExpenseCategory.Subscriptions),
        (@"(WHOLE\s*FOODS|TRADER|KROGER|SAFEWAY|PUBLIX|WEGMANS|ALDI|COSTCO\s*WHSE|GROCERY)", ExpenseCategory.Groceries),
        (@"(RESTAURANT|CAFE|COFFEE|STARBUCK|DOORDASH|UBER\s*EATS|GRUBHUB)", ExpenseCategory.Dining),
        (@"(UBER\s*TRIP|LYFT|SHELL|CHEVRON|EXXON|PARKING|MTA|METRO)", ExpenseCategory.Transport),
        (@"(PHARMACY|CVS|WALGREENS|RITE\s*AID|HOSPITAL|CLINIC|DENTAL|DOCTOR)", ExpenseCategory.Healthcare),
        (@"(CHASE\s*CREDIT|CHASE\s*CRD|CHASE\s*CARD|CHASE\s*EPAY)", ExpenseCategory.ChaseCredit),
        (@"(TEXANS\s*CU|TEXANS\s*CREDIT|TEXANS\s*FED|TEXANS\s*FCU|TEXANSCREDIT)", ExpenseCategory.TexansCreditUnion)
    ];

    private static readonly Regex[] Compiled = Rules
        .Select(r => new Regex(r.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    public ExpenseCategory Classify(string description, decimal normalizedExpenseAmount, StatementSource source)
    {
        var d = description.ToUpperInvariant();

        // Chase / bank statement vendor hints (run before generic rules).
        if (d.Contains("MUSTANG", StringComparison.Ordinal) || d.Contains("MUSTANG SUD", StringComparison.Ordinal))
            return ExpenseCategory.Water;
        if (d.Contains("COSERV", StringComparison.Ordinal) || d.Contains("CO SERV", StringComparison.Ordinal) || d.Contains("CO-SERV", StringComparison.Ordinal))
            return ExpenseCategory.Utilities;
        if (d.Contains("ATMOS", StringComparison.Ordinal))
            return ExpenseCategory.Utilities;

        if (d.Contains("MORTGAGE", StringComparison.Ordinal) || d.Contains("MTG", StringComparison.Ordinal))
            return ExpenseCategory.Mortgage;

        if (IsMortgageAmountBand(normalizedExpenseAmount) &&
            (source == StatementSource.BankOfAmerica || d.Contains("MORT", StringComparison.Ordinal) || d.Contains("LOAN", StringComparison.Ordinal)))
            return ExpenseCategory.Mortgage;

        for (var i = 0; i < Compiled.Length; i++)
        {
            if (Compiled[i].IsMatch(description))
                return Rules[i].Cat;
        }

        return ExpenseCategory.Uncategorized;
    }

    private bool IsMortgageAmountBand(decimal amount) =>
        amount >= _opt.MortgageAmountMin && amount <= _opt.MortgageAmountMax;

    public static string ComputeDedupeHash(DateOnly date, decimal amount, string description, string fileName, string userId = "")
    {
        var norm = Regex.Replace(description.ToUpperInvariant(), @"\s+", " ").Trim();
        var payload = $"{date:O}|{amount.ToString(CultureInfo.InvariantCulture)}|{norm}|{fileName.ToUpperInvariant()}|{userId}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}
