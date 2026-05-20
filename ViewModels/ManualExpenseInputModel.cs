using System.ComponentModel.DataAnnotations;
using BudgetApp.Models;

namespace BudgetApp.ViewModels;

public sealed class ManualExpenseInputModel
{
    [Required]
    public string MonthYm { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "999999")]
    public decimal Amount { get; set; }

    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
}
