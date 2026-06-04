using BudgetApp.Data;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class DashboardController(SpendingService spending, CurrentUserService currentUser, BudgetDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? ym, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var userId       = currentUser.UserId;
        var month        = MonthHelper.ParseMonth(ym);
        var available    = await spending.GetAvailableMonthsAsync(householdIds, ct).ConfigureAwait(false);
        var current      = await spending.GetMonthAsync(month, householdIds, ct).ConfigureAwait(false);

        // Previous month for deltas
        var prevMonth = month.AddMonths(-1);
        var prev      = await spending.GetMonthAsync(prevMonth, householdIds, ct).ConfigureAwait(false);

        // 6-month trend
        var trend = await spending.GetMonthlyTrendAsync(month, 6, householdIds, ct).ConfigureAwait(false);

        // Budget goals
        var goals = await db.BudgetGoals
            .Where(g => g.UserId == userId)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        // Cash flow forecast: project full-month spend from daily rate
        decimal? forecast = null;
        if (current.TotalIncome > 0 || current.TotalSpend > 0)
        {
            var today       = DateOnly.FromDateTime(DateTime.Today);
            var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
            var daysPassed  = month.Month == today.Month && month.Year == today.Year
                ? today.Day
                : daysInMonth;
            if (daysPassed > 0)
            {
                var dailyRate      = current.TotalSpend / daysPassed;
                var projectedSpend = dailyRate * daysInMonth;
                forecast = current.TotalIncome - projectedSpend;
            }
        }

        // Recent months for "Recent Months" section (exclude current)
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
            MonthYm         = MonthHelper.FormatYm(month),
            MonthLabel      = month.ToString("MMMM yyyy"),
            Current         = current,
            PrevMonth       = prev,
            Recent          = recent,
            AvailableMonths = available,
            BudgetGoals     = goals,
            MonthlyTrend    = trend,
            CashFlowForecast = forecast,
            IsFirstMonth    = !available.Any(a => MonthHelper.ParseMonth(a) < month)
        };
        return View(vm);
    }
}
