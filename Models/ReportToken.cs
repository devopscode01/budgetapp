namespace BudgetApp.Models;

/// <summary>Shareable end-of-month report link. Token is a random hex string; no auth required to view.</summary>
public sealed class ReportToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public DateOnly Month { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddDays(90);
    public string SharedByName { get; set; } = "";
}
