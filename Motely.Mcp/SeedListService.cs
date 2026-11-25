namespace Motely.Mcp;

/// <summary>Reads the bot's seed stream: a file, a CSV column, a list, or one seed.</summary>
internal static class SeedListService
{
    private static readonly char[] Separators = [',', '\n', '\r', '\t', ' '];

    /// <summary>
    /// File paths (plain lists, CSVs, motely output) and inline lists both arrive as one string.
    /// A file wins over an inline seed only when the path actually exists, so a seed named like a
    /// local file is never silently opened.
    /// </summary>
    public static bool TryRead(string? input, out string[] seeds, out string? error)
    {
        seeds = [];
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Error: seeds are required — pass a seed file, or a comma-separated seed list.";
            return false;
        }

        IEnumerable<string> raw = McpPathResolver.TryFindFile(input!.Trim(), out string resolved)
            ? ReadFile(resolved)
            : input.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (string entry in raw)
        {
            string token = entry.Trim();
            if (token.Length == 0 || token.StartsWith("#", StringComparison.Ordinal))
                continue;
            if (token is "\"\"" or "''")
                token = string.Empty;

            if (!CliSearchMode.TryNormalizeSeed(token, out string? seed))
            {
                error = $"Error: '{entry}' is not a Balatro seed ({CliSearchMode.SeedRule}).";
                return false;
            }
            if (seen.Add(seed!))
                result.Add(seed!);
        }

        if (result.Count == 0)
        {
            error = "Error: no Balatro seeds found in that list or file.";
            return false;
        }

        seeds = [.. result];
        error = null;
        return true;
    }

    private static IEnumerable<string> ReadFile(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;
            int comma = trimmed.IndexOf(',');
            yield return comma < 0 ? trimmed : trimmed[..comma];
        }
    }
}
