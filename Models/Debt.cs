namespace BudgetApp.Models;

public enum DebtType { CreditCard, AutoLoan, PersonalLoan, Other }

public sealed class Debt
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string CreditorName { get; set; } = string.Empty;
    public DebtType Type { get; set; }
    public decimal Balance { get; set; }
    public decimal MinimumPayment { get; set; }
    public decimal InterestRate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string AddedByName { get; set; } = string.Empty;
}
