namespace BudgetApp.Models;

/// <summary>Monthly spending limit for a category. Drives dashboard progress bars and over-budget alerts.</summary>
public sealed class BudgetGoal
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    /// <summary>ExpenseCategory enum value (int), including user categories ≥ 100.</summary>
    public int Category { get; set; }
    public decimal MonthlyLimit { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
