using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class DebtController(BudgetDbContext db, CurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var debts = await db.Debts
            .Where(d => d.UserId == userId)
            .OrderBy(d => d.IsActive ? 0 : 1)
            .ThenBy(d => d.Type)
            .ThenBy(d => d.CreditorName)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        var active = debts.Where(d => d.IsActive).ToList();
        var vm = new DebtVm
        {
            ActiveDebts = active,
            PaidOffDebts = debts.Where(d => !d.IsActive).ToList(),
            TotalBalance = active.Sum(d => d.Balance),
            TotalMinPayment = active.Sum(d => d.MinimumPayment)
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(
        [FromForm] string creditorName,
        [FromForm] DebtType type,
        [FromForm] decimal balance,
        [FromForm] decimal minimumPayment,
        [FromForm] decimal interestRate,
        [FromForm] string? dueDate,
        [FromForm] string? notes,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(creditorName))
        {
            DateOnly? due = DateOnly.TryParse(dueDate, out var d) ? d : null;
            db.Debts.Add(new Debt
            {
                UserId = currentUser.UserId,
                CreditorName = creditorName.Trim(),
                Type = type,
                Balance = balance,
                MinimumPayment = minimumPayment,
                InterestRate = interestRate,
                DueDate = due,
                Notes = notes?.Trim() ?? "",
                IsActive = true,
                UpdatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromForm] decimal balance,
        [FromForm] decimal minimumPayment,
        [FromForm] decimal interestRate,
        [FromForm] string? dueDate,
        [FromForm] string? notes,
        CancellationToken ct)
    {
        var debt = await db.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == currentUser.UserId, ct)
            .ConfigureAwait(false);
        if (debt is not null)
        {
            debt.Balance = balance;
            debt.MinimumPayment = minimumPayment;
            debt.InterestRate = interestRate;
            debt.DueDate = DateOnly.TryParse(dueDate, out var d) ? d : null;
            debt.Notes = notes?.Trim() ?? debt.Notes;
            debt.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPaid(int id, CancellationToken ct)
    {
        var debt = await db.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == currentUser.UserId, ct)
            .ConfigureAwait(false);
        if (debt is not null)
        {
            debt.IsActive = false;
            debt.Balance = 0;
            debt.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var debt = await db.Debts
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == currentUser.UserId, ct)
            .ConfigureAwait(false);
        if (debt is not null)
        {
            db.Debts.Remove(debt);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index));
    }
}
