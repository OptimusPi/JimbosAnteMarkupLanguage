namespace Motely.CLI;

/// <summary>
/// Path checks for DuckDB-backed seed sources and result sinks. Kept apart from the DuckDB code
/// so the NativeAOT build, which leaves DuckDB out, still recognises those paths and can say why
/// they are unavailable.
/// </summary>
public static class DuckDbPaths
{
    public static bool IsRemote(string path) =>
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("s3://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The extension after any query string or fragment, lowercased. A signed remote URL keeps its
    /// query string, so the extension comes from the URI path rather than the whole address.
    /// </summary>
    public static string SourceExtension(string path)
    {
        string withoutQuery = path;
        int suffix = path.IndexOfAny(['?', '#']);
        if (suffix >= 0)
            withoutQuery = path[..suffix];
        return Path.GetExtension(withoutQuery).ToLowerInvariant();
    }

    /// <summary>True when the argument names a Parquet, DuckDB, SQLite, or remote seed source.</summary>
    public static bool IsSeedSource(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return false;
        if (IsRemote(argument))
            return true;
        return SourceExtension(argument)
            is ".parquet"
                or ".pq"
                or ".duckdb"
                or ".db"
                or ".sqlite"
                or ".sqlite3";
    }
}
