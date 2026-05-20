namespace BudgetApp.Services;

/// <summary>Prevents path traversal when combining user input with the inbox root.</summary>
public static class InboxPathGuard
{
    public const int MaxFolderDepth = 12;

    public static bool TryGetSafeDirectoryUnderInbox(
        string inboxRootFull,
        string? relativeUserPath,
        out string destinationDirectoryFull,
        out string normalizedRelative,
        out string? error)
    {
        destinationDirectoryFull = "";
        normalizedRelative = "";
        error = null;

        var inbox = Path.GetFullPath(inboxRootFull);
        if (string.IsNullOrWhiteSpace(relativeUserPath))
        {
            destinationDirectoryFull = inbox;
            normalizedRelative = "";
            return true;
        }

        var s = relativeUserPath.Trim()
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (s.Contains("..", StringComparison.Ordinal))
        {
            error = "Folder path cannot contain '..'.";
            return false;
        }

        var parts = s.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > MaxFolderDepth)
        {
            error = "Folder path is too deep.";
            return false;
        }

        foreach (var p in parts)
        {
            if (p is "." or "..")
            {
                error = "Invalid path segment.";
                return false;
            }

            if (p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "Folder name contains invalid characters.";
                return false;
            }
        }

        var combined = inbox;
        foreach (var p in parts)
            combined = Path.Combine(combined, p);

        destinationDirectoryFull = Path.GetFullPath(combined);
        if (!IsUnderInbox(inbox, destinationDirectoryFull))
        {
            error = "Folder must stay inside the inbox.";
            return false;
        }

        normalizedRelative = string.Join("/", parts);
        return true;
    }

    private static bool IsUnderInbox(string inboxFull, string candidateFull)
    {
        var root = Path.GetFullPath(inboxFull).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var child = Path.GetFullPath(candidateFull).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(root, child, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = root + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }
}
