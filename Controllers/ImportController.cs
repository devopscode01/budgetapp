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
        string? msg     = TempData["ImportMsg"]     as string;
        bool?   success = TempData["ImportSuccess"] is bool b ? b : null;
        int     count   = TempData["ImportCount"]   is int c  ? c : 0;
        int     skipped = TempData["ImportSkipped"] is int s  ? s : 0;
        string? log     = TempData["ImportLog"]     as string;

        var vm = await BuildVmAsync(ym, msg, success, count, skipped, log, ct).ConfigureAwait(false);
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
        SetTempData(string.Join(" | ", result.Messages), result.Saved > 0, result.Saved, result.Skipped, null);
        return RedirectToAction(nameof(Index), new { ym });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(string? ym, CancellationToken ct)
    {
        var month  = MonthHelper.ParseMonth(ym);
        var folder = MonthInboxFolderFactory.MonthFolderName(month);
        var run    = await etl.RunAsync(month.Year, folder, currentUser.UserId, ct).ConfigureAwait(false);
        var msg    = run.Success
            ? $"Imported {run.TransactionsInserted} transactions ({run.TransactionsSkippedDuplicate} duplicates skipped) from {run.FilesSeen} PDF(s)."
            : "Import encountered errors — check the log below.";
        SetTempData(msg, run.Success, run.TransactionsInserted, run.TransactionsSkippedDuplicate, run.Log);
        return RedirectToAction(nameof(Index), new { ym });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAll(CancellationToken ct)
    {
        var run = await etl.RunAsync(null, null, currentUser.UserId, ct).ConfigureAwait(false);
        var msg = run.Success
            ? $"Full import: {run.TransactionsInserted} transactions inserted, {run.TransactionsSkippedDuplicate} duplicates skipped, {run.FilesSeen} PDF(s) scanned."
            : "Import encountered errors — check the log below.";
        SetTempData(msg, run.Success, run.TransactionsInserted, run.TransactionsSkippedDuplicate, run.Log);
        return RedirectToAction(nameof(Index));
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
            SetTempData("No file selected.", false, 0, 0, null);
            return RedirectToAction(nameof(Index), new { ym });
        }
        if (!file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            SetTempData("Only .txt files are supported for this import.", false, 0, 0, null);
            return RedirectToAction(nameof(Index), new { ym });
        }

        using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        var (inserted, skipped) = await etl.ImportTxtAsync(text, currentUser.UserId, ct).ConfigureAwait(false);
        SetTempData($"TXT import: {inserted} transaction(s) added, {skipped} duplicate(s) skipped.", inserted > 0 || skipped > 0, inserted, skipped, null);
        return RedirectToAction(nameof(Index), new { ym });
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
            SetTempData("No file selected.", false, 0, 0, null);
            return RedirectToAction(nameof(Index), new { ym });
        }

        var ext      = Path.GetExtension(file.FileName).ToLowerInvariant();
        var mimeType = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", _ => null };
        if (mimeType is null)
        {
            SetTempData("Only PNG and JPG screenshots are supported.", false, 0, 0, null);
            return RedirectToAction(nameof(Index), new { ym });
        }

        var llmConfig = await db.LlmConfigs
            .Where(c => c.UserId == currentUser.UserId && c.IsEnabled)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (llmConfig is null)
        {
            SetTempData("No active AI advisor configured. Enable one in Account → AI Advisor settings.", false, 0, 0, null);
            return RedirectToAction(nameof(Index), new { ym });
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct).ConfigureAwait(false);

        var (lines, error) = await llm.ExtractTransactionsFromImageAsync(llmConfig, ms.ToArray(), mimeType, ct).ConfigureAwait(false);
        if (error is not null)
        {
            SetTempData($"OCR failed: {error}", false, 0, 0, null);
            return RedirectToAction(nameof(Index), new { ym });
        }
        if (lines is null || lines.Count == 0)
        {
            SetTempData("No transactions found in the screenshot.", false, 0, 0, null);
            return RedirectToAction(nameof(Index), new { ym });
        }

        var (inserted, skipped) = await etl.ImportFromOcrAsync(lines, currentUser.UserId, ct).ConfigureAwait(false);
        SetTempData($"Screenshot OCR: {inserted} transaction(s) added, {skipped} duplicate(s) skipped.", inserted > 0 || skipped > 0, inserted, skipped, null);
        return RedirectToAction(nameof(Index), new { ym });
    }

    private void SetTempData(string msg, bool success, int count, int skipped, string? log)
    {
        TempData["ImportMsg"]     = msg;
        TempData["ImportSuccess"] = success;
        TempData["ImportCount"]   = count;
        TempData["ImportSkipped"] = skipped;
        TempData["ImportLog"]     = log;
    }

    private async Task<ImportVm> BuildVmAsync(string? ym, string? msg, bool? success, int imported, int skipped, string? etlLog, CancellationToken ct)
    {
        var householdIds = await currentUser.GetHouseholdUserIdsAsync(db, ct).ConfigureAwait(false);
        var available    = await spending.GetAvailableMonthsAsync(householdIds, ct).ConfigureAwait(false);
        return new ImportVm
        {
            MonthYm             = MonthHelper.FormatYm(MonthHelper.ParseMonth(ym)),
            Inbox               = etl.GetInboxOverview(),
            ResultMessage       = msg,
            ResultSuccess       = success,
            TransactionsImported = imported,
            TransactionsSkipped = skipped,
            EtlLog              = etlLog,
            AvailableMonths     = available
        };
    }
}
