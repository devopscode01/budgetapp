using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public sealed class SpendingService(BudgetDbContext db)
{
    public async Task<MonthSpendingVm> GetMonthAsync(DateOnly month, IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        var end = start.AddMonths(1);

        var transactions = await db.ParsedTransactions
            .Where(t => userIds.Contains(t.UserId) && t.PostedDate >= start && t.PostedDate < end && !t.IsSplit)
            .OrderBy(t => t.PostedDate)
            .ThenBy(t => t.Description)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        var manual = await db.ManualExpenses
            .Where(m => userIds.Contains(m.UserId) && m.Month >= start && m.Month < end)
            .OrderBy(m => m.Description)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        return BuildVm(start, transactions, manual);
    }

    public async Task<IReadOnlyList<CategoryTotal>> GetTrendsAsync(
        DateOnly throughMonth, int monthCount, IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        var start = throughMonth.AddMonths(-(monthCount - 1));
        start = new DateOnly(start.Year, start.Month, 1);
        var end = new DateOnly(throughMonth.Year, throughMonth.Month, 1).AddMonths(1);

        var txns = await db.ParsedTransactions
            .Where(t => userIds.Contains(t.UserId) && t.PostedDate >= start && t.PostedDate < end && !t.IsSplit)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        return txns
            .Where(t => t.Category != ExpenseCategory.Income)
            .GroupBy(t => t.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(t => t.Amount), g.Count()))
            .OrderByDescending(c => c.Amount)
            .ToList();
    }

    /// <summary>Returns per-month totals for the last <paramref name="monthCount"/> months (for trend charts).</summary>
    public async Task<IReadOnlyList<MonthTrendPoint>> GetMonthlyTrendAsync(
        DateOnly throughMonth, int monthCount, IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        var months = Enumerable.Range(0, monthCount)
            .Select(i => new DateOnly(throughMonth.Year, throughMonth.Month, 1).AddMonths(-(monthCount - 1 - i)))
            .ToList();

        var start = months[0];
        var end = new DateOnly(throughMonth.Year, throughMonth.Month, 1).AddMonths(1);

        var txns = await db.ParsedTransactions
            .Where(t => userIds.Contains(t.UserId) && t.PostedDate >= start && t.PostedDate < end && !t.IsSplit)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        var manuals = await db.ManualExpenses
            .Where(m => userIds.Contains(m.UserId) && m.Month >= start && m.Month < end)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        return months.Select(m =>
        {
            var mEnd = m.AddMonths(1);
            var spend = txns.Where(t => t.PostedDate >= m && t.PostedDate < mEnd && t.Category != ExpenseCategory.Income).Sum(t => t.Amount)
                       + manuals.Where(mx => mx.Month >= m && mx.Month < mEnd && mx.Category != ExpenseCategory.Income).Sum(mx => mx.Amount);
            var income = txns.Where(t => t.PostedDate >= m && t.PostedDate < mEnd && t.Category == ExpenseCategory.Income).Sum(t => t.Amount)
                        + manuals.Where(mx => mx.Month >= m && mx.Month < mEnd && mx.Category == ExpenseCategory.Income).Sum(mx => mx.Amount);
            return new MonthTrendPoint(m, spend, income);
        }).ToList();
    }

    public async Task<IReadOnlyList<ParsedTransaction>> SearchAsync(
        string query, IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        var q = query.Trim();
        if (string.IsNullOrEmpty(q)) return [];

        return await db.ParsedTransactions
            .Where(t => userIds.Contains(t.UserId) && !t.IsSplit &&
                (EF.Functions.Like(t.Description, $"%{q}%") ||
                 (t.Alias != null && EF.Functions.Like(t.Alias, $"%{q}%")) ||
                 (t.Notes != null && EF.Functions.Like(t.Notes, $"%{q}%"))))
            .OrderByDescending(t => t.PostedDate)
            .Take(200)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<ParsedTransaction?> FindTransactionAsync(int id, IReadOnlyList<string> userIds, CancellationToken ct = default) =>
        await db.ParsedTransactions
            .FirstOrDefaultAsync(t => t.Id == id && userIds.Contains(t.UserId), ct)
            .ConfigureAwait(false);

    public async Task<ManualExpense?> FindManualExpenseAsync(int id, IReadOnlyList<string> userIds, CancellationToken ct = default) =>
        await db.ManualExpenses
            .FirstOrDefaultAsync(m => m.Id == id && userIds.Contains(m.UserId), ct)
            .ConfigureAwait(false);

    public void Delete(ParsedTransaction tx) => db.ParsedTransactions.Remove(tx);

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<string>> GetAvailableMonthsAsync(IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        var txMonths = await db.ParsedTransactions
            .Where(t => userIds.Contains(t.UserId))
            .Select(t => new DateOnly(t.PostedDate.Year, t.PostedDate.Month, 1))
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        var manMonths = await db.ManualExpenses
            .Where(m => userIds.Contains(m.UserId))
            .Select(m => new DateOnly(m.Month.Year, m.Month.Month, 1))
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);

        return txMonths.Concat(manMonths)
            .Append(thisMonth)
            .Distinct()
            .OrderByDescending(d => d)
            .Select(d => MonthHelper.FormatYm(d))
            .ToList();
    }

    private static MonthSpendingVm BuildVm(DateOnly start, List<ParsedTransaction> transactions, List<ManualExpense> manual)
    {
        var categoryTotals = transactions
            .GroupBy(t => t.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(t => t.Amount), g.Count()))
            .Concat(manual.GroupBy(m => m.Category)
                .Select(g => new CategoryTotal(g.Key, g.Sum(m => m.Amount), g.Count())))
            .GroupBy(c => c.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(x => x.Amount), g.Sum(x => x.Count)))
            .OrderByDescending(c => c.Amount)
            .ToList();

        var total  = categoryTotals.Where(c => c.Category != ExpenseCategory.Income).Sum(c => c.Amount);
        var income = categoryTotals.Where(c => c.Category == ExpenseCategory.Income).Sum(c => c.Amount);

        return new MonthSpendingVm
        {
            Month          = start,
            MonthYm        = MonthHelper.FormatYm(start),
            MonthLabel     = start.ToString("MMMM yyyy"),
            TotalSpend     = total,
            TotalIncome    = income,
            CategoryTotals = categoryTotals,
            Transactions   = transactions,
            ManualExpenses = manual
        };
    }
}
