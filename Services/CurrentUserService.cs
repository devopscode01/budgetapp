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

    /// <summary>Effective household ID from cookie claim (fast path). Null means no claim — use GetHouseholdUserIdsAsync which does DB lookup.</summary>
    public string HouseholdId =>
        Principal?.FindFirstValue("household_id")
        ?? UserId;

    /// <summary>Returns all user IDs that share this user's household (including self). Falls back to DB lookup for JWT-authenticated requests.</summary>
    public async Task<IReadOnlyList<string>> GetHouseholdUserIdsAsync(BudgetDbContext db, CancellationToken ct)
    {
        string hid;
        var claimHid = Principal?.FindFirstValue("household_id");
        if (claimHid is not null)
        {
            hid = claimHid;
        }
        else
        {
            // JWT auth path: no household_id claim, look up BudgetUser to get household
            var uid = UserId;
            var bu = await db.BudgetUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == uid, ct).ConfigureAwait(false);
            hid = bu?.HouseholdId ?? uid;
        }

        return await db.BudgetUsers
            .Where(u => u.HouseholdId == hid || (u.HouseholdId == null && u.Id == hid))
            .Select(u => u.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
