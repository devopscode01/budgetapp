using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Options;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BudgetApp.Services;

public sealed record PdfUploadResult(int Saved, int Skipped, IReadOnlyList<string> Messages);

public sealed class BudgetEtlService(
    IOptions<BudgetOptions> options,
    BudgetDbContext db,
    PdfTextExtractor pdf,
    ExpenseClassifier classifier,
    IWebHostEnvironment env,
    ILogger<BudgetEtlService> logger)
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private readonly BudgetOptions _opt = options.Value;

    public string ResolvePath(string relativeOrAbsolute)
    {
        if (Path.IsPathRooted(relativeOrAbsolute)) return relativeOrAbsolute;
        return Path.Combine(env.ContentRootPath, relativeOrAbsolute);
    }

    public static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace("\\", "/", StringComparison.Ordinal);

    public InboxOverviewViewModel GetInboxOverview()
    {
        var inbox = ResolvePath(_opt.StatementInboxPath);
        if (!Directory.Exists(inbox))
        {
            return new InboxOverviewViewModel
            {
                AbsoluteInboxPath = inbox,
                ScanRecursively = _opt.ScanInboxRecursively,
                TotalPdfCount = 0,
                Folders = [],
                SampleRelativePdfPaths = [],
                TopLevelSubfolders = []
            };
        }

        var topLevel = Directory.EnumerateDirectories(inbox)
            .Select(d => NormalizeRelativePath(Path.GetRelativePath(inbox, d)))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var search = _opt.ScanInboxRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var fullPaths = Directory.EnumerateFiles(inbox, "*.pdf", search).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var groups = fullPaths
            .Select(f => (Full: f, Rel: NormalizeRelativePath(Path.GetRelativePath(inbox, f))))
            .GroupBy(x => GetFolderKey(x.Rel))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new InboxFolderRow(
                RelativeFolder: g.Key == "." ? "(inbox root)" : g.Key,
                PdfCount: g.Count()))
            .ToList();

        return new InboxOverviewViewModel
        {
            AbsoluteInboxPath = Path.GetFullPath(inbox),
            ScanRecursively = _opt.ScanInboxRecursively,
            TotalPdfCount = fullPaths.Count,
            Folders = groups,
            SampleRelativePdfPaths = fullPaths
                .Take(40)
                .Select(f => NormalizeRelativePath(Path.GetRelativePath(inbox, f)))
                .ToList(),
            TopLevelSubfolders = topLevel
        };
    }

    private static string GetFolderKey(string relativePdfPath)
    {
        var dir = Path.GetDirectoryName(relativePdfPath);
        if (string.IsNullOrEmpty(dir)) return ".";
        return NormalizeRelativePath(dir);
    }

    /// <summary>Creates <c>january_yyyy</c>-style folders under the inbox (idempotent).</summary>
    public IReadOnlyList<string> EnsureMonthFolders(int startYear, int endYear)
    {
        var inbox = ResolvePath(_opt.StatementInboxPath);
        Directory.CreateDirectory(inbox);
        return MonthInboxFolderFactory.EnsureEnglishMonthFolders(inbox, startYear, endYear);
    }

    public bool TryCreateInboxSubfolder(string? relativeUserPath, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(relativeUserPath))
        {
            message = "Enter a folder path to create (for example chase_inbox or january_2026/scans).";
            return false;
        }

        var inbox = ResolvePath(_opt.StatementInboxPath);
        Directory.CreateDirectory(inbox);
        var inboxFull = Path.GetFullPath(inbox);
        if (!InboxPathGuard.TryGetSafeDirectoryUnderInbox(inboxFull, relativeUserPath, out var dest, out var norm, out var err))
        {
            message = err;
            return false;
        }

        Directory.CreateDirectory(dest);
        message = string.IsNullOrEmpty(norm)
            ? "Inbox root already exists."
            : $"Folder ready: {norm}";
        return true;
    }

    public bool TryDeleteInboxSubfolder(string? relativeUserPath, bool deleteContents, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(relativeUserPath))
        {
            message = "Choose a folder to delete (or type a custom path).";
            return false;
        }

        var inbox = ResolvePath(_opt.StatementInboxPath);
        Directory.CreateDirectory(inbox);
        var inboxFull = Path.GetFullPath(inbox);
        if (!InboxPathGuard.TryGetSafeDirectoryUnderInbox(inboxFull, relativeUserPath, out var dest, out var norm, out var err))
        {
            message = err;
            return false;
        }

        if (string.Equals(Path.GetFullPath(dest), inboxFull, StringComparison.OrdinalIgnoreCase))
        {
            message = "Cannot delete the inbox root itself.";
            return false;
        }

        if (!Directory.Exists(dest))
        {
            message = $"Folder does not exist: {norm}";
            return false;
        }

        if (!deleteContents)
        {
            if (Directory.EnumerateFileSystemEntries(dest).Any())
            {
                message =
                    "Folder is not empty. Remove files first, or check \"Delete including all files and subfolders\" to remove everything.";
                return false;
            }

            Directory.Delete(dest, recursive: false);
            message = $"Deleted empty folder: {norm}";
            return true;
        }

        Directory.Delete(dest, recursive: true);
        message = $"Deleted folder and all contents: {norm}";
        return true;
    }

    /// <summary>Lists PDF paths for ETL. <paramref name="inboxSubfolderScope"/> is a path under the inbox (e.g. april_2026), or null for full inbox rules.</summary>
    public (IReadOnlyList<string> Paths, string? ErrorMessage) GetPdfPathsForEtl(string? inboxSubfolderScope)
    {
        var inboxRootFull = Path.GetFullPath(ResolvePath(_opt.StatementInboxPath));
        Directory.CreateDirectory(inboxRootFull);
        if (string.IsNullOrWhiteSpace(inboxSubfolderScope))
        {
            var so = _opt.ScanInboxRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return (Directory.EnumerateFiles(inboxRootFull, "*.pdf", so).ToList(), null);
        }

        if (!InboxPathGuard.TryGetSafeDirectoryUnderInbox(inboxRootFull, inboxSubfolderScope, out var scoped, out _, out var err))
            return ([], err);

        if (!Directory.Exists(scoped))
            return ([], null);

        return (Directory.EnumerateFiles(scoped, "*.pdf", SearchOption.AllDirectories).ToList(), null);
    }

    public int CountPdfsInScope(string? inboxSubfolderScope)
    {
        var (paths, _) = GetPdfPathsForEtl(inboxSubfolderScope);
        return paths.Count;
    }

    public async Task<EtlRun> RunAsync(
        int? defaultYear = null,
        string? inboxSubfolderScope = null,
        string userId = "",
        CancellationToken ct = default)
    {
        var year = defaultYear ?? DateTime.UtcNow.Year;
        var inbox = ResolvePath(_opt.StatementInboxPath);
        var inboxRootFull = Path.GetFullPath(inbox);
        var processed = ResolvePath(_opt.StatementProcessedPath);
        var processedFull = Path.GetFullPath(processed);
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(processedFull);

        var run = new EtlRun { StartedUtc = DateTime.UtcNow, UserId = userId };
        db.EtlRuns.Add(run);

        var log = new StringBuilder();
        try
        {
            log.AppendLine($"Inbox: {inboxRootFull}");
            log.AppendLine(
                string.IsNullOrWhiteSpace(inboxSubfolderScope)
                    ? $"Scope: entire inbox (recursive={_opt.ScanInboxRecursively})"
                    : $"Scope: subfolder \"{inboxSubfolderScope}\" (always recursive under scope)");

            var (paths, err) = GetPdfPathsForEtl(inboxSubfolderScope);
            if (err is not null)
            {
                log.AppendLine(err);
                run.FilesSeen = 0;
                run.Success = false;
                run.Log = ClampForEtlRunLog(log.ToString());
                return run;
            }

            var files = paths.ToList();
            var pendingHashes = new HashSet<string>(StringComparer.Ordinal);
            // Content keys (date|amount|description) used as secondary dedup to catch same file at different paths
            var pendingContentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            run.FilesSeen = files.Count;
            log.AppendLine($"PDF files in scope: {files.Count}");

            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                var relFromInbox = NormalizeRelativePath(Path.GetRelativePath(inboxRootFull, path));
                var text = pdf.ExtractAllText(path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    log.AppendLine($"SKIP (no text): {relFromInbox}");
                    continue;
                }

                var fromName = StatementDetection.DetectFromFileName(relFromInbox);
                var source = StatementDetection.DetectFromText(text, fromName);
                if (source == StatementSource.Unknown)
                    log.AppendLine($"WARN: Unknown source, using generic parser: {relFromInbox}");

                var lines = StatementLineParser.Parse(source, text, year);

                var (linesInserted, linesSkipped) = await PersistLinesAsync(
                    lines, source, relFromInbox, userId, pendingHashes, pendingContentKeys, ct).ConfigureAwait(false);
                run.TransactionsInserted += linesInserted;
                run.TransactionsSkippedDuplicate += linesSkipped;
                log.AppendLine($"{relFromInbox}: {lines.Count} parsed ({source}) → {linesInserted} new, {linesSkipped} duplicate");

                if (_opt.MoveFilesAfterImport)
                {
                    var dest = Path.Combine(processedFull, relFromInbox);
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);
                    if (File.Exists(dest))
                        dest = Path.Combine(
                            Path.GetDirectoryName(dest)!,
                            $"{Path.GetFileNameWithoutExtension(dest)}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
                    File.Move(path, dest);
                    log.AppendLine($"Moved to processed: {NormalizeRelativePath(Path.GetRelativePath(processedFull, dest))}");
                }
            }

            run.Success = true;
            run.Log = ClampForEtlRunLog(log.ToString());

            // Auto-match newly inserted transactions against active bills
            if (run.TransactionsInserted > 0)
                await AutoMatchBillsAsync(userId, log, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            run.Success = false;
            log.AppendLine("ERROR: " + ex.Message);
            run.Log = ClampForEtlRunLog(log.ToString());
            logger.LogError(ex, "ETL failed");
        }
        finally
        {
            run.FinishedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return run;
    }

    /// <summary>Deletes parsed PDF transactions for a calendar month scoped to the given user.</summary>
    public Task<int> DeleteParsedTransactionsForMonthAsync(DateOnly month, string userId, CancellationToken ct = default)
    {
        var start = month;
        var end = month.AddMonths(1);
        return db.ParsedTransactions
            .Where(t => t.UserId == userId && t.PostedDate >= start && t.PostedDate < end)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Saves uploaded PDFs under the inbox. Custom folder field overrides the dropdown when set.</summary>
    public async Task<PdfUploadResult> UploadPdfFilesAsync(
        IReadOnlyList<IFormFile> files,
        string? targetFolderSelect,
        string? targetFolderCustom,
        CancellationToken ct = default)
    {
        var messages = new List<string>();
        var inbox = ResolvePath(_opt.StatementInboxPath);
        Directory.CreateDirectory(inbox);
        var inboxFull = Path.GetFullPath(inbox);

        var custom = targetFolderCustom?.Trim();
        var sel = targetFolderSelect?.Trim();
        var raw = !string.IsNullOrEmpty(custom) ? custom : sel ?? "";

        if (!InboxPathGuard.TryGetSafeDirectoryUnderInbox(inboxFull, raw, out var destDir, out _, out var dirError))
        {
            messages.Add(dirError ?? "Invalid folder.");
            return new PdfUploadResult(0, files?.Count ?? 0, messages);
        }

        Directory.CreateDirectory(destDir);
        var maxBytes = _opt.MaxUploadBytesPerFile;
        if (maxBytes <= 0) maxBytes = 52_428_800L;

        var saved = 0;
        var skipped = 0;
        var nonEmpty = files.Where(f => f is { Length: > 0 }).ToList();
        if (nonEmpty.Count == 0)
        {
            messages.Add("No files selected.");
            return new PdfUploadResult(0, 0, messages);
        }

        foreach (var file in nonEmpty)
        {
            if (file.Length > maxBytes)
            {
                messages.Add($"Skipped (too large, max {maxBytes} bytes): {Path.GetFileName(file.FileName)}");
                skipped++;
                continue;
            }

            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrEmpty(name) || !name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                messages.Add($"Skipped (not a .pdf): {name}");
                skipped++;
                continue;
            }

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                messages.Add($"Skipped (bad file name): {name}");
                skipped++;
                continue;
            }

            var destPath = Path.Combine(destDir, name);
            var replaced = File.Exists(destPath);

            await using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            {
                await file.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            saved++;
            var rel = NormalizeRelativePath(Path.GetRelativePath(inboxFull, destPath));
            messages.Add(replaced ? $"Updated (replaced existing): {rel}" : $"Saved {rel}");
        }

        return new PdfUploadResult(saved, skipped, messages);
    }

    /// <summary>
    /// Normalize so positive means money out (expense), using the sign convention of each institution.
    /// BofA, txt exports, and OCR all use negative = debit convention so we take Abs().
    /// </summary>
    private static decimal ToExpenseAmount(decimal signedFromStatement, StatementSource source)
    {
        return source switch
        {
            StatementSource.ChaseCredit => signedFromStatement <= 0 ? 0m : signedFromStatement,
            StatementSource.BankOfAmerica or
            StatementSource.BofATextExport or
            StatementSource.OcrScreenshot => Math.Abs(signedFromStatement),
            _ => signedFromStatement < 0 ? decimal.Negate(signedFromStatement) : signedFromStatement
        };
    }

    private async Task<(int Inserted, int Skipped)> PersistLinesAsync(
        IReadOnlyList<RawStatementLine> lines,
        StatementSource source,
        string sourceRef,
        string userId,
        HashSet<string> pendingHashes,
        HashSet<string> pendingContentKeys,
        CancellationToken ct)
    {
        var inserted = 0;
        var skipped = 0;

        foreach (var line in lines)
        {
            var isIncome = line.SignedAmount > 0 && source is
                StatementSource.BankOfAmerica or
                StatementSource.BofATextExport or
                StatementSource.OcrScreenshot;
            var expenseAmount = ToExpenseAmount(line.SignedAmount, source);
            if (expenseAmount <= 0) continue;

            var hash = ExpenseClassifier.ComputeDedupeHash(line.Date, expenseAmount, line.Description, sourceRef, userId);
            var clampedDesc = ClampForParsedDescription(line.Description);
            var descPrefix = DescDedupePrefix(clampedDesc);
            var contentKey = $"{line.Date:O}|{expenseAmount.ToString(CultureInfo.InvariantCulture)}|{descPrefix}|{userId}";
            var likePattern = descPrefix.Replace("%", @"\%") + "%";

            // AI learning: use manually overridden category from a past import if description prefix matches
            var learnedCat = isIncome ? null : await db.ParsedTransactions
                .Where(t => t.UserId == userId && t.CategoryOverridden && EF.Functions.Like(t.Description, likePattern))
                .Select(t => (ExpenseCategory?)t.Category)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            // User-defined category keyword matching (runs before default classifier)
            ExpenseCategory? userCatMatch = null;
            if (!isIncome && learnedCat is null)
            {
                var userCats = await db.UserCategories
                    .Where(c => c.UserId == userId && c.Keywords != "")
                    .AsNoTracking()
                    .ToListAsync(ct).ConfigureAwait(false);

                var descLower = line.Description.ToLowerInvariant();
                foreach (var uc in userCats.OrderBy(c => c.SortOrder).ThenBy(c => c.Id))
                {
                    var keywords = uc.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (keywords.Any(kw => descLower.Contains(kw.ToLowerInvariant())))
                    {
                        userCatMatch = (ExpenseCategory)uc.Id;
                        break;
                    }
                }
            }

            var cat = isIncome
                ? ExpenseCategory.Income
                : learnedCat ?? userCatMatch ?? classifier.Classify(line.Description, expenseAmount, source);

            if (pendingHashes.Contains(hash) ||
                pendingContentKeys.Contains(contentKey) ||
                await db.ParsedTransactions.AnyAsync(t => t.DedupeHash == hash, ct).ConfigureAwait(false) ||
                await db.ParsedTransactions.AnyAsync(t =>
                    t.UserId == userId &&
                    t.PostedDate == line.Date &&
                    t.Amount == expenseAmount &&
                    t.Description == clampedDesc, ct).ConfigureAwait(false) ||
                await db.ParsedTransactions.AnyAsync(t =>
                    t.UserId == userId &&
                    t.PostedDate == line.Date &&
                    t.Amount == expenseAmount &&
                    EF.Functions.Like(t.Description, likePattern), ct).ConfigureAwait(false))
            {
                skipped++;
                continue;
            }

            db.ParsedTransactions.Add(new ParsedTransaction
            {
                UserId = userId,
                PostedDate = line.Date,
                Description = clampedDesc,
                Amount = expenseAmount,
                Category = cat,
                Source = source,
                SourceFileName = ClampForSourceFileName(sourceRef),
                DedupeHash = hash,
                CategoryOverridden = false
            });
            pendingHashes.Add(hash);
            pendingContentKeys.Add(contentKey);
            inserted++;
        }

        return (inserted, skipped);
    }

    public async Task<(int Inserted, int Skipped)> ImportTxtAsync(string text, string userId, CancellationToken ct)
    {
        var lines = StatementLineParser.Parse(StatementSource.BofATextExport, text, DateTime.UtcNow.Year);
        var pendingHashes = new HashSet<string>(StringComparer.Ordinal);
        var pendingContentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (inserted, skipped) = await PersistLinesAsync(
            lines, StatementSource.BofATextExport, "txt_import", userId, pendingHashes, pendingContentKeys, ct).ConfigureAwait(false);
        if (inserted > 0)
            await AutoMatchBillsAsync(userId, new StringBuilder(), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (inserted, skipped);
    }

    public async Task<(int Inserted, int Skipped)> ImportFromOcrAsync(IReadOnlyList<RawStatementLine> lines, string userId, CancellationToken ct)
    {
        var pendingHashes = new HashSet<string>(StringComparer.Ordinal);
        var pendingContentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (inserted, skipped) = await PersistLinesAsync(
            lines, StatementSource.OcrScreenshot, "screenshot_import", userId, pendingHashes, pendingContentKeys, ct).ConfigureAwait(false);
        if (inserted > 0)
            await AutoMatchBillsAsync(userId, new StringBuilder(), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (inserted, skipped);
    }

    /// <summary>Parses a folder name like "may_2026" into the first day of that month. Returns null if unrecognised.</summary>
    private static DateOnly? TryParseMonthFromFolderName(string folderName)
    {
        var top = folderName.Split('/')[0].Trim();
        var parts = top.Split('_');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[1], out var year) || year < 2000 || year > 2100) return null;
        for (var m = 1; m <= 12; m++)
        {
            if (string.Equals(English.DateTimeFormat.GetMonthName(m), parts[0], StringComparison.OrdinalIgnoreCase))
                return new DateOnly(year, m, 1);
        }
        return null;
    }

    private async Task AutoMatchBillsAsync(string userId, StringBuilder log, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthKey = new DateOnly(today.Year, today.Month, 1);

        var bills = await db.BillAlerts
            .Where(b => b.UserId == userId && b.IsActive)
            .Include(b => b.Payments.Where(p => p.Month == monthKey))
            .ToListAsync(ct).ConfigureAwait(false);

        if (bills.Count == 0) return;

        var recentTxs = await db.ParsedTransactions
            .Where(t => t.UserId == userId && t.PostedDate >= monthKey && t.PostedDate < monthKey.AddMonths(1))
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var bill in bills)
        {
            if (bill.Payments.Any(p => p.Month == monthKey)) continue;

            var billNorm = bill.Name.ToLowerInvariant().Replace(" ", "");
            var matched = recentTxs.FirstOrDefault(t =>
            {
                var desc = (t.Alias ?? t.Description).ToLowerInvariant().Replace(" ", "");
                return desc.Contains(billNorm) || billNorm.Contains(desc[..Math.Min(desc.Length, 8)]);
            });

            if (matched is null) continue;

            var amount = bill.Amount ?? matched.Amount;
            var deductedDebt = false;

            if (bill.LinkedDebtId.HasValue && amount > 0)
            {
                var debt = await db.Debts.FindAsync(new object[] { bill.LinkedDebtId.Value }, ct).ConfigureAwait(false);
                if (debt is not null && debt.UserId == userId)
                {
                    debt.Balance = Math.Max(0, debt.Balance - amount);
                    debt.UpdatedUtc = DateTime.UtcNow;
                    deductedDebt = true;
                }
            }

            db.BillPayments.Add(new BillPayment
            {
                BillAlertId         = bill.Id,
                Month               = monthKey,
                Amount              = amount,
                AcknowledgedByName  = "Auto-matched from statement",
                DebtDeducted        = deductedDebt,
                LinkedTransactionId = matched.Id,
            });

            log.AppendLine($"Auto-matched bill \"{bill.Name}\" to transaction \"{matched.Description}\" (${amount:F2})");
        }
    }

    // Strips BofA TXT reference numbers (e.g. " #000331462") and ACH trailing codes,
    // then returns the first 20 uppercase chars for cross-source dedup.
    private static readonly Regex TxRefPattern = new(@"\s+#\d{5,}", RegexOptions.Compiled);
    private static string DescDedupePrefix(string desc)
    {
        var s = TxRefPattern.Replace(desc, " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        const int maxPrefix = 20;
        return (s.Length > maxPrefix ? s[..maxPrefix] : s).ToUpperInvariant();
    }

    private static string ClampForParsedDescription(string description)
    {
        const int max = 512;
        if (string.IsNullOrEmpty(description)) return "";
        return description.Length <= max ? description : description[..max];
    }

    private static string ClampForEtlRunLog(string logText)
    {
        const int max = 7900;
        if (string.IsNullOrEmpty(logText)) return logText;
        return logText.Length <= max ? logText : logText[..max] + "\n…(log truncated)";
    }

    private static string ClampForSourceFileName(string relativePath)
    {
        const int max = 260;
        if (string.IsNullOrEmpty(relativePath)) return "";
        return relativePath.Length <= max ? relativePath : relativePath[..max];
    }
}
