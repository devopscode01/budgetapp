namespace BudgetApp.Models;

public sealed class EtlRun
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedUtc { get; set; }

    public int FilesSeen { get; set; }

    public int TransactionsInserted { get; set; }

    public int TransactionsSkippedDuplicate { get; set; }

    public string? Log { get; set; }

    public bool Success { get; set; }
}
