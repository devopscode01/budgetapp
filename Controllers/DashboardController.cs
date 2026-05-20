using BudgetApp.Data;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class DashboardController(SpendingService spending, CurrentUserService currentUser, BudgetDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? ym, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var month = MonthHelper.ParseMonth(ym);
        var available = await spending.GetAvailableMonthsAsync(householdIds, ct).ConfigureAwait(false);
        var current = await spending.GetMonthAsync(month, householdIds, ct).ConfigureAwait(false);

        var recentMonths = available
            .Select(MonthHelper.ParseMonth)
            .Where(m => m < month)
            .Take(2)
            .ToList();

        var recent = new List<MonthSpendingVm>();
        foreach (var m in recentMonths)
            recent.Add(await spending.GetMonthAsync(m, householdIds, ct).ConfigureAwait(false));

        var vm = new DashboardVm
        {
            MonthYm = MonthHelper.FormatYm(month),
            MonthLabel = month.ToString("MMMM yyyy"),
            Current = current,
            Recent = recent,
            AvailableMonths = available
        };
        return View(vm);
    }
}
