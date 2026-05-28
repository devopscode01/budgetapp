namespace BudgetApp.Models;

public enum ExpenseCategory
{
    Uncategorized = 0,
    Utilities = 1,
    Insurance = 2,
    Water = 3,
    Subscriptions = 4,
    Mortgage = 5,
    Groceries = 6,
    Dining = 7,
    Transport = 8,
    Healthcare = 9,
    Other = 10,

    /// <summary>Money in (use manual lines or re-categorize imports).</summary>
    Income = 11,

    /// <summary>Savings / transfers to savings (shown separately on summary report).</summary>
    Savings = 12,

    /// <summary>Chase credit card payment.</summary>
    ChaseCredit = 13,

    /// <summary>Texans Credit Union payment or transfer.</summary>
    TexansCreditUnion = 14
}
