using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public sealed class SpendingService(BudgetDbContext db)
{
    public async Task<MonthSpendingVm> GetMonthAsync(DateOnly month, string userId, CancellationToken ct = default)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        var end = start.AddMonths(1);

        var transactions = await db.ParsedTransactions
            .Where(t => t.UserId == userId && t.PostedDate >= start && t.PostedDate < end)
            .OrderBy(t => t.PostedDate)
            .ThenBy(t => t.Description)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        var manual = await db.ManualExpenses
            .Where(m => m.UserId == userId && m.Month >= start && m.Month < end)
            .OrderBy(m => m.Description)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        var categoryTotals = transactions
            .GroupBy(t => t.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(t => t.Amount), g.Count()))
            .Concat(manual.GroupBy(m => m.Category)
                .Select(g => new CategoryTotal(g.Key, g.Sum(m => m.Amount), g.Count())))
            .GroupBy(ct2 => ct2.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(x => x.Amount), g.Sum(x => x.Count)))
            .OrderByDescending(c => c.Amount)
            .ToList();

        var total = categoryTotals.Where(c => c.Category != ExpenseCategory.Income).Sum(c => c.Amount);
        var income = categoryTotals.Where(c => c.Category == ExpenseCategory.Income).Sum(c => c.Amount);

        return new MonthSpendingVm
        {
            Month = start,
            MonthYm = MonthHelper.FormatYm(start),
            MonthLabel = start.ToString("MMMM yyyy"),
            TotalSpend = total,
            TotalIncome = income,
            CategoryTotals = categoryTotals,
            Transactions = transactions,
            ManualExpenses = manual
        };
    }

    public async Task<ParsedTransaction?> FindTransactionAsync(int id, string userId, CancellationToken ct = default) =>
        await db.ParsedTransactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct)
            .ConfigureAwait(false);

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<string>> GetAvailableMonthsAsync(string userId, CancellationToken ct = default)
    {
        var txMonths = await db.ParsedTransactions
            .Where(t => t.UserId == userId)
            .Select(t => new DateOnly(t.PostedDate.Year, t.PostedDate.Month, 1))
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        var manMonths = await db.ManualExpenses
            .Where(m => m.UserId == userId)
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
}
