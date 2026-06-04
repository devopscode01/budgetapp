namespace BudgetApp.Models;

/// <summary>Expense line imported from a PDF statement. Amount is always stored as a positive number for money going out.</summary>
public sealed class ParsedTransaction
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateOnly PostedDate { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Positive = spending / payment out (normalized from statement).</summary>
    public decimal Amount { get; set; }

    public ExpenseCategory Category { get; set; }

    public StatementSource Source { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    /// <summary>SHA256 hex of date|amount|normalized description|file|userId — prevents duplicate imports per user.</summary>
    public string DedupeHash { get; set; } = string.Empty;

    public bool CategoryOverridden { get; set; }

    /// <summary>User-assigned readable alias (e.g. "Atmos Energy Gas"). Null = use Description for display.</summary>
    public string? Alias { get; set; }

    /// <summary>Optional user note attached to this transaction.</summary>
    public string? Notes { get; set; }

    /// <summary>True when this transaction has been split into children — excluded from totals.</summary>
    public bool IsSplit { get; set; }

    /// <summary>For split child rows: the Id of the parent transaction this was split from.</summary>
    public int? SplitFromId { get; set; }
}
