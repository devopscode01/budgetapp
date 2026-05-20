using System.Security.Claims;
using BudgetApp.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor)
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public string UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No authenticated user.");

    public bool TryGetUserId(out string userId)
    {
        userId = Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        return !string.IsNullOrEmpty(userId);
    }

    public string DisplayName =>
        Principal?.FindFirstValue("preferred_username")
        ?? Principal?.FindFirstValue(ClaimTypes.Name)
        ?? Principal?.FindFirstValue(ClaimTypes.Email)
        ?? "User";

    public string Email => Principal?.FindFirstValue(ClaimTypes.Email) ?? "";

    public bool IsApproved => Principal?.FindFirstValue("budget_approved") == "true";

    public bool IsAdmin => Principal?.FindFirstValue("budget_admin") == "true";

    /// <summary>Effective household ID — own userId for solo users, or shared ID for household members.</summary>
    public string HouseholdId =>
        Principal?.FindFirstValue("household_id")
        ?? UserId;

    /// <summary>Returns all user IDs that share this user's household (including self).</summary>
    public async Task<IReadOnlyList<string>> GetHouseholdUserIdsAsync(BudgetDbContext db, CancellationToken ct)
    {
        var hid = HouseholdId;
        return await db.BudgetUsers
            .Where(u => u.HouseholdId == hid || (u.HouseholdId == null && u.Id == hid))
            .Select(u => u.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
