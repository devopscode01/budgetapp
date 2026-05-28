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
    decimal PrevIncome, decimal PrevSpend,
    IReadOnlyList<ApiCategory> Categories,
    IReadOnlyList<ApiTransaction> Transactions);

public record ApiCategory(string Name, string Color, decimal Amount, int Count, int Pct);

// Description = display name (alias if set, else original). OriginalDescription = raw bank text (null for manual).
public record ApiTransaction(int Id, string Date, string Description, string? OriginalDescription, string Category, string Color, decimal Amount);

public record ApiDebt(int Id, string CreditorName, string Type, decimal Balance,
    decimal MinPayment, decimal InterestRate, string? DueDate, string Notes, bool IsActive, string AddedByName);

public record ApiAsset(int Id, string Name, string Type, decimal Value, string Notes, string AddedByName);

public record ApiNetWorth(decimal TotalAssets, decimal TotalDebts, decimal NetWorth,
    IReadOnlyList<ApiAsset> Assets);

public record AddDebtRequest(string CreditorName, int Type, decimal Balance,
    decimal MinPayment, decimal InterestRate, string? DueDate, string? Notes);

public record UpdateDebtRequest(string? CreditorName, decimal Balance, decimal MinPayment, decimal InterestRate,
    string? DueDate, string? Notes);

public record AddAssetRequest(string Name, int Type, decimal Value, string? Notes);
public record UpdateAssetRequest(string? Name, decimal Value, string? Notes);

public record AddManualRequest(string Description, decimal Amount, int Category, string? Ym);
public record UpdateTransactionRequest(string? Description, decimal Amount, int Category, string? Alias = null);
public record SuggestAliasResponse(string Suggestion);

public record OcrTransactionItem(string Date, string Description, decimal Amount);
public record OcrConfirmRequest(IReadOnlyList<OcrTransactionItem> Transactions);

public record ApiCategoryDto(int Id, string Name, string Color, string Keywords, bool IsBuiltIn, int SortOrder);
public record CreateCategoryRequest(string Name, string? Color, string? Keywords, int SortOrder);
public record UpdateCategoryRequest(string? Name, string? Color, string? Keywords, int SortOrder);

// ── Controller ────────────────────────────────────────────────────────────────

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BudgetApiController(
    BudgetDbContext db,
    SpendingService spending,
    CurrentUserService currentUser,
    IConfiguration config,
    IHttpClientFactory httpFactory,
    ILogger<BudgetApiController> logger,
    LlmService llm,
    BudgetEtlService etl) : ControllerBase
{
    // ── Auth ──────────────────────────────────────────────────────────────────

    [HttpPost("auth/token"), AllowAnonymous]
    public async Task<IActionResult> Token([FromBody] TokenRequest req, CancellationToken ct)
    {
        var kc = config.GetSection("Keycloak");
        logger.LogInformation("Mobile login attempt for user '{Username}' using client '{ClientId}'",
            req.Username, kc["ClientId"]);

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
        {
            logger.LogWarning("Mobile login failed for '{Username}': HTTP {Status} — {Body}",
                req.Username, (int)resp.StatusCode, body);
            return Unauthorized(new { error = "Invalid credentials" });
        }

        logger.LogInformation("Mobile login succeeded for '{Username}'", req.Username);
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

        // Previous month for delta comparison
        var prevVm = await spending.GetMonthAsync(month.AddMonths(-1), hids, ct).ConfigureAwait(false);

        var txDtos = vm.Transactions.Take(200).Select(t => new ApiTransaction(
            t.Id, t.PostedDate.ToString("yyyy-MM-dd"),
            t.Alias ?? t.Description,
            t.Alias != null ? t.Description : null,
            t.Category.ToString(), CatColor(t.Category), t.Amount)).ToList();

        var total = vm.TotalSpend > 0 ? vm.TotalSpend : 1m;
        var catDtos = vm.CategoryTotals
            .Where(c => c.Category != ExpenseCategory.Income)
            .OrderByDescending(c => c.Amount)
            .Select(c => new ApiCategory(
                c.Category.ToString(), CatColor(c.Category), c.Amount, c.Count,
                (int)Math.Round(c.Amount / total * 100))).ToList();

        return Ok(new ApiMonthSummary(vm.MonthYm, vm.MonthLabel,
            vm.TotalIncome, vm.TotalSpend, prevVm.TotalIncome, prevVm.TotalSpend, catDtos, txDtos));
    }

    [HttpPut("transactions/{id:int}")]
    public async Task<IActionResult> UpdateTransaction(int id, [FromBody] UpdateTransactionRequest req, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var tx = await spending.FindTransactionAsync(id, hids, ct).ConfigureAwait(false);
        if (tx is null) return NotFound();
        // Alias is the user-friendly display name; Description stays as original bank text
        tx.Alias = string.IsNullOrWhiteSpace(req.Alias) ? null : req.Alias.Trim();
        if (req.Amount > 0) tx.Amount = req.Amount;
        tx.Category = (ExpenseCategory)req.Category;
        tx.CategoryOverridden = true;
        await spending.SaveAsync(ct).ConfigureAwait(false);
        var display = tx.Alias ?? tx.Description;
        return Ok(new ApiTransaction(tx.Id, tx.PostedDate.ToString("yyyy-MM-dd"), display,
            tx.Alias != null ? tx.Description : null,
            tx.Category.ToString(), CatColor(tx.Category), tx.Amount));
    }

    [HttpPost("transactions/{id:int}/suggest-alias")]
    public async Task<IActionResult> SuggestAlias(int id, CancellationToken ct)
    {
        var (hids, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var tx = await spending.FindTransactionAsync(id, hids, ct).ConfigureAwait(false);
        if (tx is null) return NotFound();

        var cfg = await db.LlmConfigs.FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct).ConfigureAwait(false);
        if (cfg is null || !cfg.IsEnabled)
            return BadRequest(new { error = "AI advisor not configured" });

        var prompt = $"Given this bank transaction description, return only a short readable name (2-5 words, title case). No explanation, just the name.\nTransaction: \"{tx.Description}\"";
        var (suggestion, error) = await llm.AnalyzeAsync(cfg, prompt, ct).ConfigureAwait(false);
        if (suggestion is null)
            return StatusCode(502, new { error = error ?? "LLM returned no response" });

        return Ok(new SuggestAliasResponse(suggestion.Trim().Trim('"')));
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
        if (!string.IsNullOrWhiteSpace(req.CreditorName)) debt.CreditorName = req.CreditorName.Trim();
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
        if (!string.IsNullOrWhiteSpace(req.Name)) asset.Name = req.Name.Trim();
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

    // ── Import ────────────────────────────────────────────────────────────────

    [HttpPost("import/upload")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> ImportUpload([FromForm] IFormFile file, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file provided." });
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only PDF files are supported." });
        var result = await etl.UploadPdfFilesAsync([file], null, null, ct).ConfigureAwait(false);
        return Ok(new { saved = result.Saved, skipped = result.Skipped, messages = result.Messages });
    }

    [HttpPost("import/run")]
    public async Task<IActionResult> ImportRun(string? ym, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        var month = MonthHelper.ParseMonth(ym);
        var folder = MonthInboxFolderFactory.MonthFolderName(month);
        var run = await etl.RunAsync(month.Year, folder, currentUser.UserId, ct).ConfigureAwait(false);
        return Ok(new
        {
            success = run.Success,
            inserted = run.TransactionsInserted,
            skipped = run.TransactionsSkippedDuplicate,
            files = run.FilesSeen
        });
    }

    [HttpPost("import/screenshot/preview")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> ImportScreenshotPreview([FromForm] IFormFile file, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file provided." });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var mimeType = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", _ => null };
        if (mimeType is null) return BadRequest(new { error = "Only PNG and JPG images are supported." });

        var llmConfig = await db.LlmConfigs
            .Where(c => c.UserId == currentUser.UserId && c.IsEnabled)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (llmConfig is null)
            return BadRequest(new { error = "No active AI advisor configured. Enable one in Account → AI Advisor." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct).ConfigureAwait(false);

        var (lines, error) = await llm.ExtractTransactionsFromImageAsync(llmConfig, ms.ToArray(), mimeType, ct).ConfigureAwait(false);
        if (error is not null) return BadRequest(new { error });

        var transactions = (lines ?? [])
            .Select(l => new { date = l.Date.ToString("yyyy-MM-dd"), description = l.Description, amount = l.SignedAmount })
            .ToList();
        return Ok(new { transactions });
    }

    [HttpPost("import/screenshot/confirm")]
    public async Task<IActionResult> ImportScreenshotConfirm([FromBody] OcrConfirmRequest req, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (req?.Transactions is null || req.Transactions.Count == 0)
            return BadRequest(new { error = "No transactions provided." });

        var lines = req.Transactions
            .Where(t => DateOnly.TryParse(t.Date, out _) && !string.IsNullOrWhiteSpace(t.Description))
            .Select(t => { DateOnly.TryParse(t.Date, out var d); return new RawStatementLine(d, t.Description, t.Amount); })
            .ToList();

        var (inserted, skipped) = await etl.ImportFromOcrAsync(lines, currentUser.UserId, ct).ConfigureAwait(false);
        return Ok(new { inserted, skipped });
    }

    [HttpPost("import/txt")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportTxt([FromForm] IFormFile file, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file provided." });
        if (!file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .txt files are supported." });

        using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        var (inserted, skipped) = await etl.ImportTxtAsync(text, currentUser.UserId, ct).ConfigureAwait(false);
        return Ok(new { inserted, skipped });
    }

    // ── Categories ────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<ApiCategoryDto> BuiltInCategories =
        Enum.GetValues<ExpenseCategory>()
            .Select(c => new ApiCategoryDto(
                (int)c,
                c switch
                {
                    ExpenseCategory.Uncategorized       => "Uncategorized",
                    ExpenseCategory.Utilities           => "Utilities",
                    ExpenseCategory.Insurance           => "Insurance",
                    ExpenseCategory.Water               => "Water",
                    ExpenseCategory.Subscriptions       => "Subscriptions",
                    ExpenseCategory.Mortgage            => "Mortgage",
                    ExpenseCategory.Groceries           => "Groceries",
                    ExpenseCategory.Dining              => "Dining",
                    ExpenseCategory.Transport           => "Transport",
                    ExpenseCategory.Healthcare          => "Healthcare",
                    ExpenseCategory.Other               => "Other",
                    ExpenseCategory.Income              => "Income",
                    ExpenseCategory.Savings             => "Savings",
                    ExpenseCategory.ChaseCredit         => "Chase Credit",
                    ExpenseCategory.TexansCreditUnion   => "Texans Credit Union",
                    ExpenseCategory.NebraskaFurnitureMart => "Nebraska Furniture Mart",
                    _ => c.ToString()
                },
                CatColor(c),
                "",
                true,
                (int)c))
            .ToList();

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();

        var userCats = await db.UserCategories
            .Where(c => c.UserId == currentUser.UserId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        var result = BuiltInCategories
            .Concat(userCats.Select(c => new ApiCategoryDto(c.Id, c.Name, c.Color, c.Keywords, false, c.SortOrder)))
            .ToList();

        return Ok(result);
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest req, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Name is required" });

        var maxId = await db.UserCategories
            .Where(c => c.UserId == currentUser.UserId)
            .Select(c => (int?)c.Id)
            .MaxAsync(ct).ConfigureAwait(false);

        // EnsureSchemaAsync sets sqlite_sequence to 99, so AUTOINCREMENT will give ≥100.
        // But in case the table is used via EF without that bootstrap, we enforce it here.
        var cat = new UserCategory
        {
            UserId   = currentUser.UserId,
            Name     = req.Name.Trim(),
            Color    = req.Color?.Trim() ?? "#6366F1",
            Keywords = req.Keywords?.Trim() ?? "",
            SortOrder = req.SortOrder
        };
        db.UserCategories.Add(cat);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // If the assigned ID is <100 (edge case: sqlite_sequence not set), reassign
        if (cat.Id < 100)
        {
            var newId = Math.Max(100, (maxId ?? 99) + 1);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE \"UserCategories\" SET \"Id\" = {newId} WHERE \"Id\" = {cat.Id}",
                cancellationToken: ct).ConfigureAwait(false);
            cat.Id = newId;
        }

        return Ok(new ApiCategoryDto(cat.Id, cat.Name, cat.Color, cat.Keywords, false, cat.SortOrder));
    }

    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] UpdateCategoryRequest req, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (id < 100) return BadRequest(new { error = "Cannot edit built-in categories" });

        var cat = await db.UserCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == currentUser.UserId, ct).ConfigureAwait(false);
        if (cat is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name)) cat.Name = req.Name.Trim();
        if (req.Color is not null) cat.Color = req.Color.Trim();
        if (req.Keywords is not null) cat.Keywords = req.Keywords.Trim();
        cat.SortOrder = req.SortOrder;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(new ApiCategoryDto(cat.Id, cat.Name, cat.Color, cat.Keywords, false, cat.SortOrder));
    }

    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        var (_, ok) = await VerifyAsync(ct);
        if (!ok) return Forbid();
        if (id < 100) return BadRequest(new { error = "Cannot delete built-in categories" });

        var cat = await db.UserCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == currentUser.UserId, ct).ConfigureAwait(false);
        if (cat is null) return NotFound();

        db.UserCategories.Remove(cat);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
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
        ExpenseCategory.Income            => "#22C55E",
        ExpenseCategory.Other             => "#64748B",
        ExpenseCategory.ChaseCredit           => "#1A56DB",
        ExpenseCategory.TexansCreditUnion     => "#0E9F6E",
        ExpenseCategory.NebraskaFurnitureMart => "#B45309",
        _                                 => "#94A3B8"
    };
}
