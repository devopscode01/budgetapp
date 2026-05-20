namespace BudgetApp.Options;

public sealed class BudgetOptions
{
    public const string SectionName = "Budget";

    public string StatementInboxPath { get; set; } = "Data/Statements/Inbox";
    public string StatementProcessedPath { get; set; } = "Data/Statements/Processed";
    public bool MoveFilesAfterImport { get; set; } = false;
    public bool ScanInboxRecursively { get; set; } = true;
    public string DatabasePath { get; set; } = "Data/budget.db";
    public long MaxUploadBytesPerFile { get; set; } = 52_428_800L;
    public decimal MortgageAmountMin { get; set; } = 2800m;
    public decimal MortgageAmountMax { get; set; } = 3050m;
}
