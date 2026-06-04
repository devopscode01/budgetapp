using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class BudgetGoalController(BudgetDbContext db, CurrentUserService currentUser) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upsert(int category, decimal limit, string? ym, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var existing = await db.BudgetGoals
            .FirstOrDefaultAsync(g => g.UserId == userId && g.Category == category, ct).ConfigureAwait(false);

        if (limit <= 0)
        {
            if (existing is not null) db.BudgetGoals.Remove(existing);
        }
        else if (existing is not null)
        {
            existing.MonthlyLimit = limit;
        }
        else
        {
            db.BudgetGoals.Add(new BudgetGoal
            {
                UserId       = userId,
                Category     = category,
                MonthlyLimit = limit,
                CreatedUtc   = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return RedirectToAction("Index", "Dashboard", new { ym });
    }

    [HttpGet]
    public async Task<IActionResult> Manage(string? ym, CancellationToken ct)
    {
        var goals = await db.BudgetGoals
            .Where(g => g.UserId == currentUser.UserId)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
        ViewBag.Ym = ym;
        return View(goals);
    }
}
