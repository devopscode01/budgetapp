using BudgetApp.Models;

namespace BudgetApp.ViewModels;

public sealed record CategoryTotal(ExpenseCategory Category, decimal Amount, int Count);
public sealed record MonthTrendPoint(DateOnly Month, decimal Spend, decimal Income);

public sealed class MonthSpendingVm
{
    public DateOnly Month { get; init; }
    public string MonthYm { get; init; } = "";
    public string MonthLabel { get; init; } = "";
    public decimal TotalSpend { get; init; }
    public decimal TotalIncome { get; init; }
    public decimal NetBalance => TotalIncome - TotalSpend;
    public IReadOnlyList<CategoryTotal> CategoryTotals { get; init; } = [];
    public IReadOnlyList<ParsedTransaction> Transactions { get; init; } = [];
    public IReadOnlyList<ManualExpense> ManualExpenses { get; init; } = [];
}

public sealed class DashboardVm
{
    public string MonthYm { get; init; } = "";
    public string MonthLabel { get; init; } = "";
    public MonthSpendingVm Current { get; init; } = new();
    public MonthSpendingVm? PrevMonth { get; init; }
    public IReadOnlyList<MonthSpendingVm> Recent { get; init; } = [];
    public IReadOnlyList<string> AvailableMonths { get; init; } = [];
    public IReadOnlyList<BudgetApp.Models.BudgetGoal> BudgetGoals { get; init; } = [];
    public IReadOnlyList<MonthTrendPoint> MonthlyTrend { get; init; } = [];
    /// <summary>Projected end-of-month balance (income - estimated full-month spend). Null if not enough data.</summary>
    public decimal? CashFlowForecast { get; init; }
    public bool IsFirstMonth { get; init; }
}

public sealed class ImportVm
{
    public string MonthYm { get; init; } = "";
    public InboxOverviewViewModel Inbox { get; init; } = new();
    public string? ResultMessage { get; init; }
    public bool? ResultSuccess { get; init; }
    public int TransactionsImported { get; init; }
    public int TransactionsSkipped { get; init; }
    public string? EtlLog { get; init; }
    public IReadOnlyList<string> AvailableMonths { get; init; } = [];
}

public sealed class ManualVm
{
    public string MonthYm { get; init; } = "";
    public string MonthLabel { get; init; } = "";
    public IReadOnlyList<ManualExpense> IncomeEntries { get; init; } = [];
    public IReadOnlyList<ManualExpense> ExpenseEntries { get; init; } = [];
    public IReadOnlyList<string> AvailableMonths { get; init; } = [];
}

public sealed class DebtVm
{
    public IReadOnlyList<Debt> ActiveDebts { get; init; } = [];
    public IReadOnlyList<Debt> PaidOffDebts { get; init; } = [];
    public decimal TotalBalance { get; init; }
    public decimal TotalMinPayment { get; init; }
}

public sealed class AssetVm
{
    public IReadOnlyList<Asset> Assets { get; init; } = [];
    public decimal TotalAssets { get; init; }
    public decimal TotalDebts { get; init; }
    public decimal NetWorth => TotalAssets - TotalDebts;
}
