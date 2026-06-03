using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class ReportController(BudgetDbContext db, SpendingService spending, CurrentUserService currentUser) : Controller
{
    /// <summary>Generates (or returns existing) share token for the given month and returns JSON {url, token}.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Share(string ym, CancellationToken ct)
    {
        var month    = MonthHelper.ParseMonth(ym);
        var userId   = currentUser.UserId;
        var userName = currentUser.DisplayName;

        // Reuse an unexpired token for the same user+month
        var existing = await db.ReportTokens
            .Where(t => t.UserId == userId && t.Month == month && t.ExpiresUtc > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedUtc)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (existing is null)
        {
            existing = new ReportToken
            {
                Id           = Guid.NewGuid().ToString("N"),
                UserId       = userId,
                Month        = month,
                CreatedUtc   = DateTime.UtcNow,
                ExpiresUtc   = DateTime.UtcNow.AddDays(90),
                SharedByName = userName
            };
            db.ReportTokens.Add(existing);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var url = Url.Action(nameof(View), "Report", new { token = existing.Id }, Request.Scheme)!;
        return Json(new { url, token = existing.Id });
    }

    /// <summary>Public report page — no auth required. Accessible by anyone with the token link.</summary>
    [AllowAnonymous]
    [HttpGet("/report/{token}")]
    public async Task<IActionResult> View(string token, CancellationToken ct)
    {
        var record = await db.ReportTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == token, ct).ConfigureAwait(false);

        if (record is null || record.ExpiresUtc < DateTime.UtcNow)
            return NotFound("This report link has expired or does not exist.");

        // Resolve household without requiring an authenticated session
        var owner = await db.BudgetUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == record.UserId, ct).ConfigureAwait(false);
        var hid          = owner?.HouseholdId ?? record.UserId;
        var householdIds = await db.BudgetUsers
            .Where(u => u.HouseholdId == hid || (u.HouseholdId == null && u.Id == hid))
            .Select(u => u.Id).ToListAsync(ct).ConfigureAwait(false);
        var vm           = await spending.GetMonthAsync(record.Month, householdIds, ct).ConfigureAwait(false);

        ViewBag.SharedByName = record.SharedByName;
        ViewBag.ExpiresUtc   = record.ExpiresUtc;
        ViewBag.IsPublic     = true;
        return View("PublicReport", vm);
    }

    /// <summary>Returns the list of other app users for the internal-share UI.</summary>
    [HttpGet]
    public async Task<IActionResult> InternalUsers(CancellationToken ct)
    {
        var me    = currentUser.UserId;
        var users = await db.BudgetUsers
            .Where(u => u.Id != me && u.IsApproved)
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
        return Json(users);
    }
}
