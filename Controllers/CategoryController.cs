using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class CategoryController(BudgetDbContext db, CurrentUserService currentUser) : Controller
{
    private static readonly IReadOnlyList<(int Id, string Name, string Color)> BuiltIns =
    [
        (0,  "Uncategorized",          "#94A3B8"),
        (1,  "Utilities",              "#14B8A6"),
        (2,  "Insurance",              "#F59E0B"),
        (3,  "Water",                  "#06B6D4"),
        (4,  "Subscriptions",          "#6366F1"),
        (5,  "Mortgage",               "#5B6EF7"),
        (6,  "Groceries",              "#10B981"),
        (7,  "Dining",                 "#F97316"),
        (8,  "Transport",              "#8B5CF6"),
        (9,  "Healthcare",             "#EF4444"),
        (10, "Other",                  "#64748B"),
        (11, "Income",                 "#22C55E"),
        (12, "Savings",                "#059669"),
        (13, "Chase Credit",           "#1A56DB"),
        (14, "Texans Credit Union",    "#0E9F6E"),
        (15, "Nebraska Furniture Mart","#B45309"),
    ];

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userCats = await db.UserCategories
            .Where(c => c.UserId == currentUser.UserId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        ViewBag.BuiltIns = BuiltIns;
        return View(userCats);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] string name,
        [FromForm] string? color,
        [FromForm] string? keywords,
        [FromForm] int sortOrder,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            db.UserCategories.Add(new UserCategory
            {
                UserId    = currentUser.UserId,
                Name      = name.Trim(),
                Color     = string.IsNullOrWhiteSpace(color) ? "#6366F1" : color.Trim(),
                Keywords  = keywords?.Trim() ?? "",
                SortOrder = sortOrder
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [FromForm] string? name,
        [FromForm] string? color,
        [FromForm] string? keywords,
        [FromForm] int sortOrder,
        CancellationToken ct)
    {
        if (id < 100) return BadRequest();
        var cat = await db.UserCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == currentUser.UserId, ct).ConfigureAwait(false);
        if (cat is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(name)) cat.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(color)) cat.Color = color.Trim();
        cat.Keywords  = keywords?.Trim() ?? "";
        cat.SortOrder = sortOrder;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (id < 100) return BadRequest();
        var cat = await db.UserCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == currentUser.UserId, ct).ConfigureAwait(false);
        if (cat is not null)
        {
            db.UserCategories.Remove(cat);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index));
    }
}
