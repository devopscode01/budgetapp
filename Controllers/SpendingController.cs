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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNote(int id, string? notes, string? ym, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var tx = await spending.FindTransactionAsync(id, householdIds, ct).ConfigureAwait(false);
        if (tx is not null)
        {
            tx.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            await spending.SaveAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index), new { ym });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SplitTransaction(int id, string? ym,
        [FromForm] List<int> splitCategory, [FromForm] List<decimal> splitAmount, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var tx = await spending.FindTransactionAsync(id, householdIds, ct).ConfigureAwait(false);
        if (tx is null || tx.IsSplit)
            return RedirectToAction(nameof(Index), new { ym });

        var parts = splitCategory.Zip(splitAmount).Where(p => p.Second > 0).ToList();
        if (parts.Count < 2) return RedirectToAction(nameof(Index), new { ym });

        tx.IsSplit = true;
        foreach (var (cat, amt) in parts)
        {
            db.Set<ParsedTransaction>().Add(new ParsedTransaction
            {
                UserId           = tx.UserId,
                PostedDate       = tx.PostedDate,
                Description      = tx.Description,
                Amount           = amt,
                Category         = (ExpenseCategory)cat,
                Source           = tx.Source,
                SourceFileName   = tx.SourceFileName,
                DedupeHash       = $"{tx.DedupeHash}_split_{cat}_{amt}",
                CategoryOverridden = true,
                SplitFromId      = tx.Id
            });
        }
        await spending.SaveAsync(ct).ConfigureAwait(false);
        return RedirectToAction(nameof(Index), new { ym });
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? ym, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var month = MonthHelper.ParseMonth(ym);
        var vm = await spending.GetMonthAsync(month, householdIds, ct).ConfigureAwait(false);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Date,Description,Category,Amount,Notes,Source");
        foreach (var t in vm.Transactions.OrderBy(t => t.PostedDate))
        {
            var desc  = $"\"{t.Description.Replace("\"", "\"\"")}\"";
            var notes = t.Notes is null ? "" : $"\"{t.Notes.Replace("\"", "\"\"")}\"";
            sb.AppendLine($"{t.PostedDate:yyyy-MM-dd},{desc},{t.Category},{t.Amount:F2},{notes},{t.Source}");
        }
        foreach (var m in vm.ManualExpenses.OrderBy(m => m.Month))
        {
            var desc = $"\"{m.Description.Replace("\"", "\"\"")}\"";
            sb.AppendLine($"{m.Month:yyyy-MM-dd},{desc},{m.Category},{m.Amount:F2},,Manual");
        }

        var bytes    = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var filename = $"budget-{month:yyyy-MM}.csv";
        return File(bytes, "text/csv", filename);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q, string? ym, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        ViewBag.Query    = q ?? "";
        ViewBag.Ym       = ym;
        if (string.IsNullOrWhiteSpace(q))
            return View("SearchResults", Array.Empty<ParsedTransaction>());

        var results = await spending.SearchAsync(q, householdIds, ct).ConfigureAwait(false);
        return View("SearchResults", results);
    }
}
