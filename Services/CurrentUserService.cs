using System.Security.Claims;

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
}
