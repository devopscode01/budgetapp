using BudgetApp.Models;

namespace BudgetApp.Services;

public static class StatementDetection
{
    public static StatementSource DetectFromFileName(string fileName)
    {
        var n = fileName.ToLowerInvariant();
        if (n.Contains("chase")) return StatementSource.ChaseCredit;
        if (n.Contains("bofa") || n.Contains("bankofamerica") || n.Contains("bank-of-america") || n.Contains("boa"))
            return StatementSource.BankOfAmerica;
        return StatementSource.Unknown;
    }

    public static StatementSource DetectFromText(string text, StatementSource fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var head = text.Length > 4000 ? text.AsSpan(0, 4000) : text.AsSpan();
        var lower = head.ToString().ToLowerInvariant();
        if (lower.Contains("jpmorgan chase") || lower.Contains("chase card") || lower.Contains("chase credit") ||
            lower.Contains("chase bank") || lower.Contains("chase.com") || lower.Contains("chase freedom") ||
            lower.Contains("chase sapphire") || lower.Contains("chase ink") || lower.Contains("chase unlimited") ||
            lower.Contains("chase flex"))
            return StatementSource.ChaseCredit;
        if (lower.Contains("bank of america") || (lower.Contains("bofa") && lower.Contains("statement")) ||
            lower.Contains("bankofamerica.com"))
            return StatementSource.BankOfAmerica;
        return fallback;
    }
}
