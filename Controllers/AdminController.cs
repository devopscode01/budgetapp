using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class AdminController(BudgetDbContext db, CurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        if (!currentUser.IsAdmin) return Forbid();
        var users = await db.BudgetUsers
            .OrderByDescending(u => u.IsAdmin)
            .ThenBy(u => u.CreatedUtc)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(string id, CancellationToken ct)
    {
        if (!currentUser.IsAdmin) return Forbid();
        var user = await db.BudgetUsers.FindAsync([id], ct).ConfigureAwait(false);
        if (user is not null)
        {
            user.IsApproved = true;
            user.ApprovedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        if (!currentUser.IsAdmin) return Forbid();
        var myId = currentUser.UserId;
        var user = await db.BudgetUsers.FindAsync([id], ct).ConfigureAwait(false);
        if (user is not null && user.Id != myId)
        {
            user.IsApproved = false;
            user.ApprovedUtc = null;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAdmin(string id, CancellationToken ct)
    {
        if (!currentUser.IsAdmin) return Forbid();
        var myId = currentUser.UserId;
        var user = await db.BudgetUsers.FindAsync([id], ct).ConfigureAwait(false);
        if (user is not null && user.Id != myId)
        {
            user.IsAdmin = !user.IsAdmin;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Users));
    }
}
