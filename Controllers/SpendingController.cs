using BudgetApp.Models;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class SpendingController(SpendingService spending, CurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? ym, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var month = MonthHelper.ParseMonth(ym);
        var vm = await spending.GetMonthAsync(month, userId, ct).ConfigureAwait(false);
        ViewBag.AvailableMonths = await spending.GetAvailableMonthsAsync(userId, ct).ConfigureAwait(false);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recategorize(int id, ExpenseCategory category, string? ym, CancellationToken ct)
    {
        var tx = await spending.FindTransactionAsync(id, currentUser.UserId, ct).ConfigureAwait(false);
        if (tx is not null)
        {
            tx.Category = category;
            tx.CategoryOverridden = true;
            await spending.SaveAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index), new { ym });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTransaction(int id, string? ym, CancellationToken ct)
    {
        var tx = await spending.FindTransactionAsync(id, currentUser.UserId, ct).ConfigureAwait(false);
        if (tx is not null)
        {
            spending.Delete(tx);
            await spending.SaveAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index), new { ym });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTransaction(int id, string? description, decimal amount, ExpenseCategory category, string? ym, CancellationToken ct)
    {
        var tx = await spending.FindTransactionAsync(id, currentUser.UserId, ct).ConfigureAwait(false);
        if (tx is not null)
        {
            if (!string.IsNullOrWhiteSpace(description)) tx.Description = description.Trim();
            if (amount > 0) tx.Amount = amount;
            tx.Category = category;
            tx.CategoryOverridden = true;
            await spending.SaveAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index), new { ym });
    }
}
