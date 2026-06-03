using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Options;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class AdminController(
    BudgetDbContext db,
    CurrentUserService currentUser,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    IOptions<BudgetOptions> options,
    IWebHostEnvironment env) : Controller
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
            user.IsApproved  = true;
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
            user.IsApproved  = false;
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken ct)
    {
        if (!currentUser.IsAdmin) return Forbid();
        if (id == currentUser.UserId) return BadRequest("Cannot delete your own account.");

        // Delete all user data in dependency order
        var billAlertIds = await db.BillAlerts
            .Where(b => b.UserId == id)
            .Select(b => b.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        if (billAlertIds.Count > 0)
            await db.BillPayments.Where(p => billAlertIds.Contains(p.BillAlertId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        await db.BillAlerts.Where(b => b.UserId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.ParsedTransactions.Where(t => t.UserId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.ManualExpenses.Where(m => m.UserId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Debts.Where(d => d.UserId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Assets.Where(a => a.UserId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.LlmConfigs.Where(l => l.UserId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.UserCategories.Where(u => u.UserId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.EtlRuns.Where(e => e.UserId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.ReportTokens.Where(r => r.UserId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // Delete from Keycloak
        var kc         = config.GetSection("Keycloak");
        var (adminUrl, realm) = SplitAuthority(kc["Authority"]!);
        var adminToken = await GetAdminTokenAsync(adminUrl, kc["AdminUsername"]!, kc["AdminPassword"]!, ct).ConfigureAwait(false);
        if (adminToken is not null)
        {
            using var client = httpFactory.CreateClient();
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Delete, $"{adminUrl}/admin/realms/{realm}/users/{id}")
                { Headers = { { "Authorization", $"Bearer {adminToken}" } } }, ct).ConfigureAwait(false);
        }

        await db.BudgetUsers.Where(u => u.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public IActionResult Backup()
    {
        if (!currentUser.IsAdmin) return Forbid();

        var dbPath   = options.Value.DatabasePath;
        var fullPath = Path.IsPathRooted(dbPath) ? dbPath : Path.Combine(env.ContentRootPath, dbPath);
        var tmp      = Path.Combine(Path.GetTempPath(), $"budget-backup-{Guid.NewGuid():N}.db");

        try
        {
#pragma warning disable EF1002
            db.Database.ExecuteSqlRaw($"VACUUM INTO '{tmp.Replace("'", "''")}'");
#pragma warning restore EF1002
            var bytes    = System.IO.File.ReadAllBytes(tmp);
            var filename = $"budget-backup-{DateTime.UtcNow:yyyy-MM-dd-HHmm}.db";
            return File(bytes, "application/octet-stream", filename);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private static (string adminUrl, string realm) SplitAuthority(string authority)
    {
        var idx   = authority.IndexOf("/realms/", StringComparison.Ordinal);
        var admin = idx >= 0 ? authority[..idx] : authority;
        var realm = idx >= 0 ? authority[(idx + 8)..].Split('/')[0] : "budget";
        return (admin, realm);
    }

    private async Task<string?> GetAdminTokenAsync(string adminUrl, string username, string password, CancellationToken ct)
    {
        try
        {
            using var client = httpFactory.CreateClient();
            var resp = await client.PostAsync(
                $"{adminUrl}/realms/master/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"]  = "admin-cli",
                    ["username"]   = username,
                    ["password"]   = password
                }), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.GetProperty("access_token").GetString();
        }
        catch { return null; }
    }
}
