namespace Motely.Mcp;

/// <summary>
/// Bots launch MCP servers from all sorts of working directories. A relative filter or seed path
/// is resolved from the current directory first, then from the server binary upward (bin/… →
/// project root), so `ColaLit` and `mySeeds.csv` work without an absolute path. Absolute paths
/// outside those two areas are refused: the tools stay read-only inside the search party's files.
/// </summary>
internal static class McpPathResolver
{
    public static bool TryFindFile(string input, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string trimmed = input.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            string full = Path.GetFullPath(trimmed);
            path = full;
            return IsAllowed(full) && File.Exists(full);
        }

        foreach (string root in Roots())
        {
            string candidate = Path.GetFullPath(Path.Combine(root, trimmed));
            if (IsAllowed(candidate) && File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), trimmed));
        return false;
    }

    /// <summary>True when input resolves outside the allowed search areas.</summary>
    public static bool IsOutsideAllowed(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        string trimmed = input.Trim();
        string full = Path.GetFullPath(
            Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(Directory.GetCurrentDirectory(), trimmed)
        );
        return !IsAllowed(full);
    }

    public static string DescribeAllowedRoots() =>
        string.Join(", ", Roots().Select(root => $"'{root}'"));

    private static IEnumerable<string> Roots()
    {
        string current = Path.GetFullPath(Directory.GetCurrentDirectory());
        string server = ServerRoot();
        return new[] { current, server }
            .Distinct(PathComparer)
            .ToArray();
    }

    private static string ServerRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Motely.slnx"))
                || File.Exists(Path.Combine(directory.FullName, "Motely.Mcp.csproj")))
                return directory.FullName;
        }
        return AppContext.BaseDirectory;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsAllowed(string full)
    {
        foreach (string root in Roots())
        {
            string rootFull = Path.GetFullPath(root);
            string rootPrefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
                || rootFull.EndsWith(Path.AltDirectorySeparatorChar)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;
            if (full.Equals(rootFull, PathComparison) || full.StartsWith(rootPrefix, PathComparison))
                return true;
        }
        return false;
    }
}
