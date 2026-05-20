using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class AssetController(BudgetDbContext db, CurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var assets = await db.Assets
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Type)
            .ThenBy(a => a.Name)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        var totalDebts = await db.Debts
            .Where(d => d.UserId == userId && d.IsActive)
            .SumAsync(d => d.Balance, ct).ConfigureAwait(false);

        var vm = new AssetVm
        {
            Assets = assets,
            TotalAssets = assets.Sum(a => a.Value),
            TotalDebts = totalDebts
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(
        [FromForm] string name,
        [FromForm] AssetType type,
        [FromForm] decimal value,
        [FromForm] string? notes,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            db.Assets.Add(new Asset
            {
                UserId = currentUser.UserId,
                Name = name.Trim(),
                Type = type,
                Value = value,
                Notes = notes?.Trim() ?? "",
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
        [FromForm] decimal value,
        [FromForm] string? notes,
        CancellationToken ct)
    {
        var asset = await db.Assets
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == currentUser.UserId, ct)
            .ConfigureAwait(false);
        if (asset is not null)
        {
            asset.Value = value;
            asset.Notes = notes?.Trim() ?? asset.Notes;
            asset.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var asset = await db.Assets
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == currentUser.UserId, ct)
            .ConfigureAwait(false);
        if (asset is not null)
        {
            db.Assets.Remove(asset);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return RedirectToAction(nameof(Index));
    }
}
