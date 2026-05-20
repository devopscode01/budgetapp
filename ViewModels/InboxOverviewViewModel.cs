namespace BudgetApp.ViewModels;

public sealed record InboxFolderRow(string RelativeFolder, int PdfCount);

/// <summary>Shows where PDFs live: absolute path, per-folder counts (including custom folders), and samples.</summary>
public sealed class InboxOverviewViewModel
{
    public string AbsoluteInboxPath { get; init; } = string.Empty;

    public bool ScanRecursively { get; init; }

    public int TotalPdfCount { get; init; }

    public IReadOnlyList<InboxFolderRow> Folders { get; init; } = [];

    public IReadOnlyList<string> SampleRelativePdfPaths { get; init; } = [];

    /// <summary>Immediate child folders of the inbox (so empty <c>january_2026</c> folders still show up).</summary>
    public IReadOnlyList<string> TopLevelSubfolders { get; init; } = [];
}
