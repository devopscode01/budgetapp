namespace BudgetApp.Models;

/// <summary>App-level user record tied to a Keycloak subject claim. Controls approval and admin status.</summary>
public sealed class BudgetUser
{
    /// <summary>Keycloak sub claim (UUID).</summary>
    public string Id { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsApproved { get; set; }

    public bool IsAdmin { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ApprovedUtc { get; set; }

    public string? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; }

    /// <summary>Null = solo user (household ID = own Id). Set to inviter's effective household ID when joining via invite link.</summary>
    public string? HouseholdId { get; set; }
}
