using BudgetApp.Data;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class ImportController(BudgetEtlService etl, SpendingService spending, CurrentUserService currentUser, BudgetDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? ym, CancellationToken ct)
    {
        var vm = await BuildVmAsync(ym, null, null, 0, 0, ct).ConfigureAwait(false);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(
        [FromForm] List<IFormFile>? files,
        [FromForm] string? targetFolder,
        string? ym,
        CancellationToken ct)
    {
        var result = await etl.UploadPdfFilesAsync(files ?? [], targetFolder, null, ct).ConfigureAwait(false);
        var msg = string.Join(" | ", result.Messages);
        var vm = await BuildVmAsync(ym, msg, result.Saved > 0, result.Saved, result.Skipped, ct).ConfigureAwait(false);
        return View(nameof(Index), vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(string? ym, CancellationToken ct)
    {
        var month = MonthHelper.ParseMonth(ym);
        var folder = MonthInboxFolderFactory.MonthFolderName(month);
        var run = await etl.RunAsync(month.Year, folder, currentUser.UserId, ct).ConfigureAwait(false);
        var msg = run.Success
            ? $"Imported {run.TransactionsInserted} transactions ({run.TransactionsSkippedDuplicate} duplicates skipped) from {run.FilesSeen} PDF(s)."
            : "Import encountered errors — check the log.";
        var vm = await BuildVmAsync(ym, msg, run.Success, run.TransactionsInserted, run.TransactionsSkippedDuplicate, ct).ConfigureAwait(false);
        return View(nameof(Index), vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAll(CancellationToken ct)
    {
        var run = await etl.RunAsync(null, null, currentUser.UserId, ct).ConfigureAwait(false);
        var msg = run.Success
            ? $"Full import: {run.TransactionsInserted} transactions inserted, {run.TransactionsSkippedDuplicate} duplicates skipped, {run.FilesSeen} PDF(s) scanned."
            : "Import encountered errors.";
        var vm = await BuildVmAsync(null, msg, run.Success, run.TransactionsInserted, run.TransactionsSkippedDuplicate, ct).ConfigureAwait(false);
        return View(nameof(Index), vm);
    }

    private async Task<ImportVm> BuildVmAsync(string? ym, string? msg, bool? success, int imported, int skipped, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var available = await spending.GetAvailableMonthsAsync(householdIds, ct).ConfigureAwait(false);
        return new ImportVm
        {
            MonthYm = MonthHelper.FormatYm(MonthHelper.ParseMonth(ym)),
            Inbox = etl.GetInboxOverview(),
            ResultMessage = msg,
            ResultSuccess = success,
            TransactionsImported = imported,
            TransactionsSkipped = skipped,
            AvailableMonths = available
        };
    }
}
