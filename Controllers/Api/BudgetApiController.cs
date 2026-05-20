using System.Text;
using System.Text.Json;
using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers.Api;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record TokenRequest(string Username, string Password);
public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);
public record RefreshRequest(string RefreshToken);

public record ApiMonthSummary(
    string MonthYm, string MonthLabel,
    decimal TotalIncome, decimal TotalSpend,
    IReadOnlyList<ApiCategory> Categories,
    IReadOnlyList<ApiTransaction> Transactions);

public record ApiCategory(string Name, string Color, decimal Amount, int Count, int Pct);
public record ApiTransaction(int Id, string Date, string Description, string Category, string Color, decimal Amount);

public record ApiDebt(int Id, string CreditorName, string Type, decimal Balance,
    decimal MinPayment, decimal InterestRate, string? DueDate, string Notes, bool IsActive, string AddedByName);

public record ApiAsset(int Id, string Name, string Type, decimal Value, string Notes, string AddedByName);

public record ApiNetWorth(decimal TotalAssets, decimal TotalDebts, decimal NetWorth,
    IReadOnlyList<ApiAsset> Assets);

public record AddDebtRequest(string CreditorName, int Type, decimal Balance,
    decimal MinPayment, decimal InterestRate, string? DueDate, string? Notes);

public record UpdateDebtRequest(decimal Balance, decimal MinPayment, decimal InterestRate,
    string? DueDate, string? Notes);

public record AddAssetRequest(string Name, int Type, decimal Value, string? Notes);
public record UpdateAssetRequest(decimal Value, string? Notes);

public record AddManualRequest(string Description, decimal Amount, int Category, string? Ym);

// ── Controller ────────────────────────────────────────────────────────────────

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BudgetApiController(
    BudgetDbContext db,
    SpendingService spending,
    CurrentUserService currentUser,
    IConfiguration config,
    IHttpClientFactory httpFactory) : ControllerBase
{
    // ── Auth ──────────────────────────────────────────────────────────────────

    [HttpPost("auth/token"), AllowAnonymous]
    public async Task<IActionResult> Token([FromBody] TokenRequest req, CancellationToken ct)
    {
        var kc = config.GetSection("Keycloak");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "password",
            ["client_id"]     = kc["ClientId"]!,
            ["client_secret"] = kc["ClientSecret"]!,
            ["username"]      = req.Username,
            ["password"]      = req.Password,
            ["scope"]         = "openid profile email offline_access"
        });

        using var http = httpFactory.CreateClient();
        var resp = await http.PostAsync($"{kc["Authority"]}/protocol/openid-connect/token", form, ct)
            .ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return Unauthorized(new { error = "Invalid credentials" });

        using var doc = JsonDocument.Parse(body);
        return Ok(new TokenResponse(
            doc.RootElement.GetProperty("access_token").GetString()!,
            doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : "",
            doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600));
    }

    [HttpPost("auth/refresh"), AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        var kc = config.GetSection("Keycloak");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["client_id"]     = kc["ClientId"]!,
            ["client_secret"] = kc["ClientSecret"]!,
            ["refresh_token"] = req.RefreshToken
        });

        using var http = httpFactory.CreateClient();
        var resp = await http.PostAsync($"{kc["Authority"]}/protocol/openid-connect/token", form, ct)
            .ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return Unauthorized(new { error = "Token refresh failed" });

        using var doc = JsonDocument.Parse(body);
        return Ok(new TokenResponse(
            doc.RootElement.GetProperty("access_token").GetString()!,
            doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : req.RefreshToken,
            doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600));
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    [HttpGet("months")]
    public async Task<IActionResult> Months(CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var months = await spending.GetAvailableMonthsAsync(hids, ct).ConfigureAwait(false);
        return Ok(months);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(string? ym, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();

        var month = MonthHelper.ParseMonth(ym);
        var vm = await spending.GetMonthAsync(month, hids, ct).ConfigureAwait(false);

        var txDtos = vm.Transactions.Take(50).Select(t => new ApiTransaction(
            t.Id, t.PostedDate.ToString("yyyy-MM-dd"), t.Description,
            t.Category.ToString(), CatColor(t.Category), t.Amount)).ToList();

        var total = vm.TotalSpend > 0 ? vm.TotalSpend : 1m;
        var catDtos = vm.CategoryTotals
            .Where(c => c.Category != ExpenseCategory.Income)
            .OrderByDescending(c => c.Amount)
            .Select(c => new ApiCategory(
                c.Category.ToString(), CatColor(c.Category), c.Amount, c.Count,
                (int)Math.Round(c.Amount / total * 100))).ToList();

        return Ok(new ApiMonthSummary(vm.MonthYm, vm.MonthLabel,
            vm.TotalIncome, vm.TotalSpend, catDtos, txDtos));
    }

    [HttpDelete("transactions/{id:int}")]
    public async Task<IActionResult> DeleteTransaction(int id, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var tx = await spending.FindTransactionAsync(id, hids, ct).ConfigureAwait(false);
        if (tx is null) return NotFound();
        spending.Delete(tx);
        await spending.SaveAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    // ── Debts ─────────────────────────────────────────────────────────────────

    [HttpGet("debts")]
    public async Task<IActionResult> Debts(CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var debts = await db.Debts
            .Where(d => hids.Contains(d.UserId))
            .OrderBy(d => d.IsActive ? 0 : 1).ThenBy(d => d.CreditorName)
            .AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return Ok(debts.Select(ToDto).ToList());
    }

    [HttpPost("debts")]
    public async Task<IActionResult> AddDebt([FromBody] AddDebtRequest req, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (string.IsNullOrWhiteSpace(req.CreditorName)) return BadRequest();
        var debt = new Debt
        {
            UserId = currentUser.UserId,
            CreditorName = req.CreditorName.Trim(),
            Type = (DebtType)req.Type,
            Balance = req.Balance,
            MinimumPayment = req.MinPayment,
            InterestRate = req.InterestRate,
            DueDate = DateOnly.TryParse(req.DueDate, out var d) ? d : null,
            Notes = req.Notes?.Trim() ?? "",
            IsActive = true,
            UpdatedUtc = DateTime.UtcNow,
            AddedByName = currentUser.DisplayName
        };
        db.Debts.Add(debt);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(debt));
    }

    [HttpPut("debts/{id:int}")]
    public async Task<IActionResult> UpdateDebt(int id, [FromBody] UpdateDebtRequest req, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var debt = await db.Debts.FirstOrDefaultAsync(d => d.Id == id && hids.Contains(d.UserId), ct)
            .ConfigureAwait(false);
        if (debt is null) return NotFound();
        debt.Balance = req.Balance;
        debt.MinimumPayment = req.MinPayment;
        debt.InterestRate = req.InterestRate;
        debt.DueDate = DateOnly.TryParse(req.DueDate, out var d) ? d : null;
        debt.Notes = req.Notes?.Trim() ?? debt.Notes;
        debt.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(debt));
    }

    [HttpDelete("debts/{id:int}")]
    public async Task<IActionResult> DeleteDebt(int id, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var debt = await db.Debts.FirstOrDefaultAsync(d => d.Id == id && hids.Contains(d.UserId), ct)
            .ConfigureAwait(false);
        if (debt is null) return NotFound();
        db.Debts.Remove(debt);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    // ── Assets ────────────────────────────────────────────────────────────────

    [HttpGet("assets")]
    public async Task<IActionResult> Assets(CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var assets = await db.Assets
            .Where(a => hids.Contains(a.UserId))
            .OrderBy(a => a.Type).ThenBy(a => a.Name)
            .AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var totalDebts = await db.Debts
            .Where(d => hids.Contains(d.UserId) && d.IsActive)
            .SumAsync(d => d.Balance, ct).ConfigureAwait(false);
        var totalAssets = assets.Sum(a => a.Value);
        return Ok(new ApiNetWorth(totalAssets, totalDebts, totalAssets - totalDebts,
            assets.Select(AssetToDto).ToList()));
    }

    [HttpPost("assets")]
    public async Task<IActionResult> AddAsset([FromBody] AddAssetRequest req, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest();
        var asset = new Asset
        {
            UserId = currentUser.UserId,
            Name = req.Name.Trim(),
            Type = (AssetType)req.Type,
            Value = req.Value,
            Notes = req.Notes?.Trim() ?? "",
            UpdatedUtc = DateTime.UtcNow,
            AddedByName = currentUser.DisplayName
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(AssetToDto(asset));
    }

    [HttpPut("assets/{id:int}")]
    public async Task<IActionResult> UpdateAsset(int id, [FromBody] UpdateAssetRequest req, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && hids.Contains(a.UserId), ct)
            .ConfigureAwait(false);
        if (asset is null) return NotFound();
        asset.Value = req.Value;
        asset.Notes = req.Notes?.Trim() ?? asset.Notes;
        asset.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(AssetToDto(asset));
    }

    [HttpDelete("assets/{id:int}")]
    public async Task<IActionResult> DeleteAsset(int id, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && hids.Contains(a.UserId), ct)
            .ConfigureAwait(false);
        if (asset is null) return NotFound();
        db.Assets.Remove(asset);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    // ── Manual expenses ───────────────────────────────────────────────────────

    [HttpPost("manual")]
    public async Task<IActionResult> AddManual([FromBody] AddManualRequest req, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Description) || req.Amount <= 0) return BadRequest();
        var month = MonthHelper.ParseMonth(req.Ym);
        db.ManualExpenses.Add(new ManualExpense
        {
            UserId = currentUser.UserId,
            Month = new DateOnly(month.Year, month.Month, 1),
            Description = req.Description.Trim(),
            Amount = req.Amount,
            Category = (ExpenseCategory)req.Category,
            CreatedUtc = DateTime.UtcNow,
            AddedByName = currentUser.DisplayName
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(IReadOnlyList<string> hids, bool ok)> VerifyAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var bu = await db.BudgetUsers.FindAsync(new object[] { userId }, ct).ConfigureAwait(false);
        if (bu is null || !bu.IsApproved) return ([], false);
        var hids = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        return (hids, true);
    }

    private static ApiDebt ToDto(Debt d) => new(
        d.Id, d.CreditorName,
        d.Type switch { DebtType.CreditCard => "Credit Card", DebtType.AutoLoan => "Auto Loan",
            DebtType.PersonalLoan => "Personal Loan", _ => "Other" },
        d.Balance, d.MinimumPayment, d.InterestRate,
        d.DueDate?.ToString("yyyy-MM-dd"), d.Notes, d.IsActive, d.AddedByName);

    private static ApiAsset AssetToDto(Asset a) => new(
        a.Id, a.Name,
        a.Type switch { AssetType.RealEstate => "Real Estate", AssetType.Stock => "Stocks/ETFs",
            AssetType.Retirement => "Retirement", AssetType.Vehicle => "Vehicle",
            AssetType.Cash => "Cash/Savings", AssetType.Bond => "Bonds", _ => "Other" },
        a.Value, a.Notes, a.AddedByName);

    private static string CatColor(ExpenseCategory c) => c switch
    {
        ExpenseCategory.Mortgage      => "#5B6EF7",
        ExpenseCategory.Utilities     => "#14B8A6",
        ExpenseCategory.Water         => "#06B6D4",
        ExpenseCategory.Groceries     => "#10B981",
        ExpenseCategory.Dining        => "#F97316",
        ExpenseCategory.Insurance     => "#F59E0B",
        ExpenseCategory.Transport     => "#8B5CF6",
        ExpenseCategory.Subscriptions => "#6366F1",
        ExpenseCategory.Healthcare    => "#EF4444",
        ExpenseCategory.Savings       => "#059669",
        ExpenseCategory.Income        => "#22C55E",
        ExpenseCategory.Other         => "#64748B",
        _                             => "#94A3B8"
    };
}
