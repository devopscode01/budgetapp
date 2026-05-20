using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class ManualController(BudgetDbContext db, SpendingService spending, CurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? ym, CancellationToken ct)
    {
        var vm = await BuildVmAsync(ym, ct).ConfigureAwait(false);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(
        string? ym,
        [FromForm] string? description,
        [FromForm] decimal amount,
        [FromForm] ExpenseCategory category,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(description) && amount > 0)
        {
            var month = MonthHelper.ParseMonth(ym);
            db.ManualExpenses.Add(new ManualExpense
            {
                UserId = currentUser.UserId,
                Month = new DateOnly(month.Year, month.Month, 1),
                Description = description.Trim(),
                Amount = amount,
                Category = category,
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index), new { ym });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? ym, CancellationToken ct)
    {
        var entry = await db.ManualExpenses
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == currentUser.UserId, ct)
            .ConfigureAwait(false);
        if (entry is not null)
        {
            db.ManualExpenses.Remove(entry);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index), new { ym });
    }

    private async Task<ManualVm> BuildVmAsync(string? ym, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var month = MonthHelper.ParseMonth(ym);
        var start = new DateOnly(month.Year, month.Month, 1);
        var end = start.AddMonths(1);
        var entries = await db.ManualExpenses
            .Where(m => m.UserId == userId && m.Month >= start && m.Month < end)
            .OrderByDescending(m => m.CreatedUtc)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
        var available = await spending.GetAvailableMonthsAsync(userId, ct).ConfigureAwait(false);
        return new ManualVm
        {
            MonthYm = MonthHelper.FormatYm(month),
            MonthLabel = month.ToString("MMMM yyyy"),
            IncomeEntries = entries.Where(e => e.Category == ExpenseCategory.Income).ToList(),
            ExpenseEntries = entries.Where(e => e.Category != ExpenseCategory.Income).ToList(),
            AvailableMonths = available
        };
    }
}
