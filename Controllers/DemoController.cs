using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class DemoController(BudgetDbContext db, CurrentUserService currentUser) : Controller
{
    private const string DemoSource = "DEMO_ACCOUNT";

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Seed(string? ym, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var month = MonthHelper.ParseMonth(ym);

        var alreadySeeded = await db.ParsedTransactions
            .AnyAsync(t => t.UserId == userId && t.SourceFileName == DemoSource &&
                           t.PostedDate.Year == month.Year && t.PostedDate.Month == month.Month, ct)
            .ConfigureAwait(false);

        if (!alreadySeeded)
        {
            var transactions = BuildTransactions(month, userId);
            db.ParsedTransactions.AddRange(transactions);

            var income = BuildIncome(month, userId);
            db.ManualExpenses.AddRange(income);

            var hasDebts = await db.Debts.AnyAsync(d => d.UserId == userId, ct).ConfigureAwait(false);
            if (!hasDebts)
                db.Debts.AddRange(BuildDebts(userId));

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return RedirectToAction("Index", "Dashboard", new { ym = MonthHelper.FormatYm(month) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(string? ym, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var month = MonthHelper.ParseMonth(ym);

        var txns = await db.ParsedTransactions
            .Where(t => t.UserId == userId && t.SourceFileName == DemoSource &&
                        t.PostedDate.Year == month.Year && t.PostedDate.Month == month.Month)
            .ToListAsync(ct).ConfigureAwait(false);
        db.ParsedTransactions.RemoveRange(txns);

        var manuals = await db.ManualExpenses
            .Where(m => m.UserId == userId && m.Month.Year == month.Year &&
                        m.Month.Month == month.Month && m.Description.StartsWith("[DEMO]"))
            .ToListAsync(ct).ConfigureAwait(false);
        db.ManualExpenses.RemoveRange(manuals);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return RedirectToAction("Index", "Dashboard", new { ym = MonthHelper.FormatYm(month) });
    }

    private static List<ManualExpense> BuildIncome(DateOnly month, string userId) =>
    [
        new ManualExpense { UserId = userId, Month = month, Description = "[DEMO] Paul — Paycheck",  Amount = 4_200m, Category = ExpenseCategory.Income, CreatedUtc = DateTime.UtcNow },
        new ManualExpense { UserId = userId, Month = month, Description = "[DEMO] Alex — Paycheck",  Amount = 2_800m, Category = ExpenseCategory.Income, CreatedUtc = DateTime.UtcNow },
    ];

    private static List<ParsedTransaction> BuildTransactions(DateOnly month, string userId)
    {
        var rows = new (int Day, string Desc, decimal Amt, ExpenseCategory Cat)[]
        {
            (1,  "Mortgage — Greenleaf Bank",       1_850.00m, ExpenseCategory.Mortgage),
            (3,  "Evergy Electric",                   124.18m, ExpenseCategory.Utilities),
            (3,  "Atmos Energy Gas",                   67.44m, ExpenseCategory.Utilities),
            (5,  "City Water & Sewer",                 58.00m, ExpenseCategory.Water),
            (2,  "Walmart Supercenter",               147.63m, ExpenseCategory.Groceries),
            (8,  "Kroger",                             198.22m, ExpenseCategory.Groceries),
            (13, "Aldi",                               134.50m, ExpenseCategory.Groceries),
            (17, "Whole Foods Market",                  83.90m, ExpenseCategory.Groceries),
            (4,  "Chipotle Mexican Grill",              32.45m, ExpenseCategory.Dining),
            (9,  "Olive Garden",                        78.12m, ExpenseCategory.Dining),
            (11, "McDonald's",                          22.80m, ExpenseCategory.Dining),
            (7,  "Starbucks",                           17.40m, ExpenseCategory.Dining),
            (16, "Pizza Hut",                           44.99m, ExpenseCategory.Dining),
            (1,  "State Farm — Auto",                  145.00m, ExpenseCategory.Insurance),
            (1,  "BCBS — Health",                       89.00m, ExpenseCategory.Insurance),
            (5,  "Shell Gas Station",                   68.00m, ExpenseCategory.Transport),
            (14, "BP Gas",                              72.00m, ExpenseCategory.Transport),
            (10, "Downtown Parking Garage",             15.00m, ExpenseCategory.Transport),
            (1,  "Netflix",                             22.99m, ExpenseCategory.Subscriptions),
            (1,  "Spotify",                             12.99m, ExpenseCategory.Subscriptions),
            (1,  "Disney+",                             13.99m, ExpenseCategory.Subscriptions),
            (1,  "Amazon Prime",                        14.99m, ExpenseCategory.Subscriptions),
            (1,  "Hulu",                                17.99m, ExpenseCategory.Subscriptions),
            (3,  "iCloud+ 200GB",                        9.99m, ExpenseCategory.Subscriptions),
            (6,  "CVS Pharmacy",                        45.00m, ExpenseCategory.Healthcare),
            (1,  "Savings Transfer — HYSA",            500.00m, ExpenseCategory.Savings),
        };

        return rows.Select(r =>
        {
            var date = new DateOnly(month.Year, month.Month, Math.Min(r.Day, DateTime.DaysInMonth(month.Year, month.Month)));
            var hash = Hash($"{date}|{r.Amt}|{r.Desc.ToLowerInvariant()}|{DemoSource}|{userId}");
            return new ParsedTransaction
            {
                UserId = userId,
                PostedDate = date,
                Description = r.Desc,
                Amount = r.Amt,
                Category = r.Cat,
                Source = StatementSource.Unknown,
                SourceFileName = DemoSource,
                DedupeHash = hash
            };
        }).ToList();
    }

    private static List<Debt> BuildDebts(string userId) =>
    [
        new Debt
        {
            UserId = userId,
            CreditorName   = "Chase Sapphire Preferred",
            Type           = DebtType.CreditCard,
            Balance        = 3_240.00m,
            MinimumPayment = 65.00m,
            InterestRate   = 24.99m,
            IsActive       = true,
            UpdatedUtc     = DateTime.UtcNow
        },
        new Debt
        {
            UserId = userId,
            CreditorName   = "Toyota Financial Services",
            Type           = DebtType.AutoLoan,
            Balance        = 14_800.00m,
            MinimumPayment = 385.00m,
            InterestRate   = 5.49m,
            DueDate        = DateOnly.FromDateTime(DateTime.Today.AddDays(12)),
            IsActive       = true,
            UpdatedUtc     = DateTime.UtcNow
        },
        new Debt
        {
            UserId = userId,
            CreditorName   = "Old Student Loan",
            Type           = DebtType.PersonalLoan,
            Balance        = 0m,
            MinimumPayment = 0m,
            Notes          = "Paid off May 2024",
            IsActive       = false,
            UpdatedUtc     = DateTime.UtcNow
        },
    ];

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
