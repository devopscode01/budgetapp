using BudgetApp.Models;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class SpendingController(SpendingService spending, CurrentUserService currentUser, BudgetApp.Data.BudgetDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? ym, string? category, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var month = MonthHelper.ParseMonth(ym);
        var vm = await spending.GetMonthAsync(month, householdIds, ct).ConfigureAwait(false);
        ViewBag.AvailableMonths = await spending.GetAvailableMonthsAsync(householdIds, ct).ConfigureAwait(false);
        ViewBag.ActiveCategory = category?.Trim();
        ViewBag.UserCategories = await db.UserCategories
            .Where(c => c.UserId == currentUser.UserId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recategorize(int id, int category, string? ym, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var tx = await spending.FindTransactionAsync(id, householdIds, ct).ConfigureAwait(false);
        if (tx is not null)
        {
            tx.Category = (ExpenseCategory)category;
            tx.CategoryOverridden = true;
            await spending.SaveAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index), new { ym });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTransaction(int id, string? ym, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var tx = await spending.FindTransactionAsync(id, householdIds, ct).ConfigureAwait(false);
        if (tx is not null)
        {
            spending.Delete(tx);
            await spending.SaveAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index), new { ym });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTransaction(int id, string? description, decimal amount, int category, string? ym, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var tx = await spending.FindTransactionAsync(id, householdIds, ct).ConfigureAwait(false);
        if (tx is not null)
        {
            if (!string.IsNullOrWhiteSpace(description)) tx.Description = description.Trim();
            if (amount > 0) tx.Amount = amount;
            tx.Category = (ExpenseCategory)category;
            tx.CategoryOverridden = true;
            await spending.SaveAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index), new { ym });
    }
}
