namespace BudgetApp.Models;

public sealed class BillAlert
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Amount { get; set; }    // null = variable amount
    public int DayOfMonth { get; set; }     // 1-28 specific day, 31 = end of month
    public int? LinkedDebtId { get; set; }  // optional link to debt for auto-deduction
    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = "";
    public string AddedByName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public Debt? LinkedDebt { get; set; }
    public ICollection<BillPayment> Payments { get; set; } = [];
}
