using BudgetApp.Data;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

[Authorize]
public sealed class ImportController(BudgetEtlService etl, SpendingService spending, CurrentUserService currentUser, BudgetDbContext db, LlmService llm) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? ym, CancellationToken ct)
    {
        var vm = await BuildVmAsync(ym, null, null, 0, 0, null, ct).ConfigureAwait(false);
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
        var vm = await BuildVmAsync(ym, msg, result.Saved > 0, result.Saved, result.Skipped, null, ct).ConfigureAwait(false);
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
            : "Import encountered errors — check the log below.";
        var vm = await BuildVmAsync(ym, msg, run.Success, run.TransactionsInserted, run.TransactionsSkippedDuplicate, run.Log, ct).ConfigureAwait(false);
        return View(nameof(Index), vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAll(CancellationToken ct)
    {
        var run = await etl.RunAsync(null, null, currentUser.UserId, ct).ConfigureAwait(false);
        var msg = run.Success
            ? $"Full import: {run.TransactionsInserted} transactions inserted, {run.TransactionsSkippedDuplicate} duplicates skipped, {run.FilesSeen} PDF(s) scanned."
            : "Import encountered errors — check the log below.";
        var vm = await BuildVmAsync(null, msg, run.Success, run.TransactionsInserted, run.TransactionsSkippedDuplicate, run.Log, ct).ConfigureAwait(false);
        return View(nameof(Index), vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadTxt(
        [FromForm] IFormFile? file,
        string? ym,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            var vm0 = await BuildVmAsync(ym, "No file selected.", false, 0, 0, null, ct).ConfigureAwait(false);
            return View(nameof(Index), vm0);
        }
        if (!file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var vm0 = await BuildVmAsync(ym, "Only .txt files are supported for this import.", false, 0, 0, null, ct).ConfigureAwait(false);
            return View(nameof(Index), vm0);
        }

        using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        var (inserted, skipped) = await etl.ImportTxtAsync(text, currentUser.UserId, ct).ConfigureAwait(false);
        var msg = $"TXT import: {inserted} transaction(s) added, {skipped} duplicate(s) skipped.";
        var vm = await BuildVmAsync(ym, msg, inserted > 0 || skipped > 0, inserted, skipped, null, ct).ConfigureAwait(false);
        return View(nameof(Index), vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadScreenshot(
        [FromForm] IFormFile? file,
        string? ym,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            var vm0 = await BuildVmAsync(ym, "No file selected.", false, 0, 0, null, ct).ConfigureAwait(false);
            return View(nameof(Index), vm0);
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var mimeType = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", _ => null };
        if (mimeType is null)
        {
            var vm0 = await BuildVmAsync(ym, "Only PNG and JPG screenshots are supported.", false, 0, 0, null, ct).ConfigureAwait(false);
            return View(nameof(Index), vm0);
        }

        var llmConfig = await db.LlmConfigs
            .Where(c => c.UserId == currentUser.UserId && c.IsEnabled)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (llmConfig is null)
        {
            var vm0 = await BuildVmAsync(ym, "No active AI advisor configured. Enable one in Account → AI Advisor settings.", false, 0, 0, null, ct).ConfigureAwait(false);
            return View(nameof(Index), vm0);
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct).ConfigureAwait(false);

        var (lines, error) = await llm.ExtractTransactionsFromImageAsync(llmConfig, ms.ToArray(), mimeType, ct).ConfigureAwait(false);
        if (error is not null)
        {
            var vm0 = await BuildVmAsync(ym, $"OCR failed: {error}", false, 0, 0, null, ct).ConfigureAwait(false);
            return View(nameof(Index), vm0);
        }
        if (lines is null || lines.Count == 0)
        {
            var vm0 = await BuildVmAsync(ym, "No transactions found in the screenshot.", false, 0, 0, null, ct).ConfigureAwait(false);
            return View(nameof(Index), vm0);
        }

        var (inserted, skipped) = await etl.ImportFromOcrAsync(lines, currentUser.UserId, ct).ConfigureAwait(false);
        var msg = $"Screenshot OCR: {inserted} transaction(s) added, {skipped} duplicate(s) skipped.";
        var vm = await BuildVmAsync(ym, msg, inserted > 0 || skipped > 0, inserted, skipped, null, ct).ConfigureAwait(false);
        return View(nameof(Index), vm);
    }

    private async Task<ImportVm> BuildVmAsync(string? ym, string? msg, bool? success, int imported, int skipped, string? etlLog, CancellationToken ct)
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
            EtlLog = etlLog,
            AvailableMonths = available
        };
    }
}
