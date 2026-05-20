namespace BudgetApp.Models;

/// <summary>User-entered expense for a calendar month (not from PDF).</summary>
public sealed class ManualExpense
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>First day of the month this expense belongs to.</summary>
    public DateOnly Month { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public ExpenseCategory Category { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string AddedByName { get; set; } = string.Empty;
}
