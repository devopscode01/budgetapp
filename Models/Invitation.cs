namespace BudgetApp.Models;

public sealed class Invitation
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public string HouseholdId { get; set; } = string.Empty;
    public string InvitedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
    public string UsedByDisplayName { get; set; } = string.Empty;
}
