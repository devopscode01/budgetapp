using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers.Api;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record ApiBill(
    int Id, string Name, decimal? Amount, int DayOfMonth, bool IsEndOfMonth,
    int? LinkedDebtId, string? LinkedDebtName, bool IsActive, string Notes,
    bool IsPaidThisMonth, int DaysUntilDue, ApiBillPaymentInfo? PaymentThisMonth);

public record ApiBillPaymentInfo(int Id, decimal Amount, string AcknowledgedAt, string By);

public record AddBillRequest(string Name, decimal? Amount, int DayOfMonth, int? LinkedDebtId, string? Notes);
public record UpdateBillRequest(string Name, decimal? Amount, int DayOfMonth, int? LinkedDebtId, string? Notes);
public record AcknowledgeBillRequest(decimal Amount);

public record ApiLlmConfig(int Provider, string Endpoint, string ApiKey, string Model, bool IsEnabled);
public record UpdateLlmConfigRequest(int Provider, string Endpoint, string ApiKey, string Model, bool IsEnabled);
public record LlmInsightResponse(string Insight, string GeneratedAt);

// ── Controller ────────────────────────────────────────────────────────────────

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BillsApiController(
    BudgetDbContext db,
    CurrentUserService currentUser,
    SpendingService spending,
    LlmService llm,
    ILogger<BillsApiController> logger) : ControllerBase
{
    // ── Bills ─────────────────────────────────────────────────────────────────

    [HttpGet("bills")]
    public async Task<IActionResult> GetBills(CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var bills = await db.BillAlerts
            .Where(b => hids.Contains(b.UserId) && b.IsActive)
            .Include(b => b.LinkedDebt)
            .Include(b => b.Payments.Where(p => p.Month >= monthStart))
            .OrderBy(b => b.DayOfMonth == 31 ? 32 : b.DayOfMonth)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        return Ok(bills.Select(b => ToDto(b, today)));
    }

    [HttpPost("bills")]
    public async Task<IActionResult> AddBill([FromBody] AddBillRequest req, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required");
        if (req.DayOfMonth < 1 || req.DayOfMonth > 31) return BadRequest("DayOfMonth must be 1-31");

        var bill = new BillAlert
        {
            UserId       = currentUser.UserId,
            Name         = req.Name.Trim(),
            Amount       = req.Amount,
            DayOfMonth   = req.DayOfMonth,
            LinkedDebtId = req.LinkedDebtId,
            Notes        = req.Notes?.Trim() ?? "",
            AddedByName  = currentUser.DisplayName,
            IsActive     = true,
        };
        db.BillAlerts.Add(bill);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await db.Entry(bill).Reference(b => b.LinkedDebt).LoadAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(bill, DateOnly.FromDateTime(DateTime.Today)));
    }

    [HttpPut("bills/{id:int}")]
    public async Task<IActionResult> UpdateBill(int id, [FromBody] UpdateBillRequest req, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();

        var bill = await db.BillAlerts
            .Include(b => b.LinkedDebt)
            .FirstOrDefaultAsync(b => b.Id == id && hids.Contains(b.UserId), ct)
            .ConfigureAwait(false);
        if (bill is null) return NotFound();

        bill.Name         = req.Name.Trim();
        bill.Amount       = req.Amount;
        bill.DayOfMonth   = req.DayOfMonth;
        bill.LinkedDebtId = req.LinkedDebtId;
        bill.Notes        = req.Notes?.Trim() ?? "";
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (bill.LinkedDebtId.HasValue && bill.LinkedDebt?.Id != bill.LinkedDebtId)
            await db.Entry(bill).Reference(b => b.LinkedDebt).LoadAsync(ct).ConfigureAwait(false);

        return Ok(ToDto(bill, DateOnly.FromDateTime(DateTime.Today)));
    }

    [HttpDelete("bills/{id:int}")]
    public async Task<IActionResult> DeleteBill(int id, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();

        var bill = await db.BillAlerts
            .FirstOrDefaultAsync(b => b.Id == id && hids.Contains(b.UserId), ct)
            .ConfigureAwait(false);
        if (bill is null) return NotFound();

        bill.IsActive = false; // soft delete
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("bills/{id:int}/acknowledge")]
    public async Task<IActionResult> AcknowledgeBill(int id, [FromBody] AcknowledgeBillRequest req, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthKey = new DateOnly(today.Year, today.Month, 1);

        var bill = await db.BillAlerts
            .Include(b => b.Payments.Where(p => p.Month == monthKey))
            .Include(b => b.LinkedDebt)
            .FirstOrDefaultAsync(b => b.Id == id && hids.Contains(b.UserId), ct)
            .ConfigureAwait(false);
        if (bill is null) return NotFound();

        // Idempotent: if already acknowledged this month, return existing
        var existing = bill.Payments.FirstOrDefault(p => p.Month == monthKey);
        if (existing is not null)
            return Ok(ToDto(bill, today));

        var deductedDebt = false;
        if (bill.LinkedDebtId.HasValue && req.Amount > 0)
        {
            var debt = bill.LinkedDebt
                ?? await db.Debts.FindAsync(new object[] { bill.LinkedDebtId.Value }, ct)
                                  .ConfigureAwait(false);
            if (debt is not null && hids.Contains(debt.UserId))
            {
                debt.Balance = Math.Max(0, debt.Balance - req.Amount);
                debt.UpdatedUtc = DateTime.UtcNow;
                deductedDebt = true;
                logger.LogInformation("Deducted {Amount} from debt {DebtId} ({Name})",
                    req.Amount, debt.Id, debt.CreditorName);
            }
        }

        var payment = new BillPayment
        {
            BillAlertId          = id,
            Month                = monthKey,
            Amount               = req.Amount,
            AcknowledgedByName   = currentUser.DisplayName,
            DebtDeducted         = deductedDebt,
        };
        db.BillPayments.Add(payment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        bill.Payments.Add(payment);
        return Ok(ToDto(bill, today));
    }

    // ── LLM Config ────────────────────────────────────────────────────────────

    [HttpGet("llm/config")]
    public async Task<IActionResult> GetLlmConfig(CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();

        var cfg = await db.LlmConfigs
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct)
            .ConfigureAwait(false);

        if (cfg is null) return Ok(new ApiLlmConfig(0, "http://localhost:11434", "", "llama3.2", false));

        return Ok(new ApiLlmConfig(cfg.Provider, cfg.Endpoint, cfg.ApiKey, cfg.Model, cfg.IsEnabled));
    }

    [HttpPut("llm/config")]
    public async Task<IActionResult> SaveLlmConfig([FromBody] UpdateLlmConfigRequest req, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();

        var cfg = await db.LlmConfigs
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct)
            .ConfigureAwait(false);

        if (cfg is null)
        {
            cfg = new LlmConfig { UserId = currentUser.UserId };
            db.LlmConfigs.Add(cfg);
        }

        cfg.Provider   = req.Provider;
        cfg.Endpoint   = req.Endpoint.Trim();
        cfg.ApiKey     = req.ApiKey.Trim();
        cfg.Model      = req.Model.Trim();
        cfg.IsEnabled  = req.IsEnabled;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Ok(new ApiLlmConfig(cfg.Provider, cfg.Endpoint, cfg.ApiKey, cfg.Model, cfg.IsEnabled));
    }

    [HttpPost("llm/analyze")]
    public async Task<IActionResult> Analyze(CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();

        var cfg = await db.LlmConfigs
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct)
            .ConfigureAwait(false);
        if (cfg is null || !cfg.IsEnabled)
            return BadRequest(new { error = "LLM not configured or disabled" });

        // Build context snapshot
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthKey = new DateOnly(today.Year, today.Month, 1);

        var summary = await spending.GetMonthAsync(
            new DateOnly(today.Year, today.Month, 1), hids, ct).ConfigureAwait(false);

        var debts = await db.Debts
            .Where(d => hids.Contains(d.UserId) && d.IsActive)
            .AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var assets = await db.Assets
            .Where(a => hids.Contains(a.UserId))
            .AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var bills = await db.BillAlerts
            .Where(b => hids.Contains(b.UserId) && b.IsActive)
            .Include(b => b.Payments.Where(p => p.Month == monthKey))
            .AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var prompt = BuildPrompt(today, summary, debts, assets, bills, monthKey);

        var (insight, error) = await llm.AnalyzeAsync(cfg, prompt, ct).ConfigureAwait(false);
        if (insight is null)
            return StatusCode(502, new { error = error ?? "LLM returned no response" });

        return Ok(new LlmInsightResponse(insight, DateTime.UtcNow.ToString("o")));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(IReadOnlyList<string> hids, bool ok)> VerifyAsync(CancellationToken ct)
    {
        var bu = await db.BudgetUsers.FindAsync(new object[] { currentUser.UserId }, ct).ConfigureAwait(false);
        if (bu is null || !bu.IsApproved) return ([], false);
        var hids = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        return (hids, true);
    }

    private static ApiBill ToDto(BillAlert b, DateOnly today)
    {
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var actualDay   = b.DayOfMonth >= 31 ? daysInMonth : Math.Min(b.DayOfMonth, daysInMonth);
        var dueDate     = new DateOnly(today.Year, today.Month, actualDay);
        var daysUntil   = dueDate.DayNumber - today.DayNumber;
        var monthKey    = new DateOnly(today.Year, today.Month, 1);
        var payment     = b.Payments.FirstOrDefault(p => p.Month == monthKey);

        return new ApiBill(
            b.Id, b.Name, b.Amount, b.DayOfMonth, b.DayOfMonth >= 31,
            b.LinkedDebtId, b.LinkedDebt?.CreditorName, b.IsActive, b.Notes,
            payment is not null, daysUntil,
            payment is null ? null : new ApiBillPaymentInfo(
                payment.Id, payment.Amount,
                payment.AcknowledgedUtc.ToString("o"),
                payment.AcknowledgedByName));
    }

    private static string BuildPrompt(
        DateOnly today,
        BudgetApp.ViewModels.MonthSpendingVm summary,
        List<Debt> debts,
        List<Asset> assets,
        List<BillAlert> bills,
        DateOnly monthKey)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a personal finance advisor. Analyze the following household budget snapshot and provide concise, actionable insights (3-4 paragraphs). Focus on: spending health, bill payment status, debt progress, and 2-3 specific recommendations.");
        sb.AppendLine();
        sb.AppendLine($"DATE: {today:MMMM d, yyyy}");
        sb.AppendLine();
        sb.AppendLine($"SPENDING — {summary.MonthLabel}:");
        sb.AppendLine($"  Income:   ${summary.TotalIncome:N2}");
        sb.AppendLine($"  Spending: ${summary.TotalSpend:N2}");
        sb.AppendLine($"  Net:      ${summary.TotalIncome - summary.TotalSpend:N2}");
        if (summary.CategoryTotals.Count > 0)
        {
            sb.AppendLine("  By category:");
            foreach (var c in summary.CategoryTotals.OrderByDescending(x => x.Amount).Take(8))
                sb.AppendLine($"    {c.Category}: ${c.Amount:N2}");
        }

        sb.AppendLine();
        sb.AppendLine("BILLS THIS MONTH:");
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        foreach (var b in bills)
        {
            var paid    = b.Payments.Any(p => p.Month == monthKey);
            var day     = b.DayOfMonth >= 31 ? daysInMonth : Math.Min(b.DayOfMonth, daysInMonth);
            var dueDate = new DateOnly(today.Year, today.Month, day);
            var status  = paid ? "PAID" : dueDate < today ? "OVERDUE" : $"due {dueDate:MMM d}";
            var amt     = b.Amount.HasValue ? $"${b.Amount:N2}" : "variable";
            sb.AppendLine($"  {(paid ? "✓" : "✗")} {b.Name} ({amt}) — {status}");
        }

        sb.AppendLine();
        sb.AppendLine("DEBTS:");
        foreach (var d in debts)
            sb.AppendLine($"  {d.CreditorName}: ${d.Balance:N2} balance, {d.InterestRate:F1}% APR, min ${d.MinimumPayment:N2}/mo");

        var totalAssets = assets.Sum(a => a.Value);
        var totalDebts  = debts.Sum(d => d.Balance);
        sb.AppendLine();
        sb.AppendLine($"NET WORTH: ${totalAssets:N2} assets − ${totalDebts:N2} debts = ${totalAssets - totalDebts:N2}");

        return sb.ToString();
    }
}
