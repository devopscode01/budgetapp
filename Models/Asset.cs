namespace BudgetApp.Models;

public enum AssetType { RealEstate, Stock, Retirement, Vehicle, Cash, Bond, Other }

public sealed class Asset
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetType Type { get; set; }
    public decimal Value { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string AddedByName { get; set; } = string.Empty;
}
