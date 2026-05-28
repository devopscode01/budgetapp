namespace BudgetApp.Models;

public sealed class BillPayment
{
    public int Id { get; set; }
    public int BillAlertId { get; set; }
    public DateOnly Month { get; set; }         // year-month this payment covers
    public decimal Amount { get; set; }
    public DateTime AcknowledgedUtc { get; set; } = DateTime.UtcNow;
    public string AcknowledgedByName { get; set; } = "";
    public bool DebtDeducted { get; set; }

    /// <summary>FK to the ParsedTransaction that paid this bill (null if manually acknowledged).</summary>
    public int? LinkedTransactionId { get; set; }

    public BillAlert Bill { get; set; } = null!;
    public ParsedTransaction? LinkedTransaction { get; set; }
}
