using System.Data.Common;
using DuckDB.NET.Data;
using Motely.SeedProviders;

namespace Motely.CLI;

/// <summary>
/// Streams seeds from any Parquet, DuckDB, SQLite, or remote HTTP(S)/S3 Parquet URL (e.g. Cloudflare R2
/// over HTTP Range requests via DuckDB httpfs) using streaming mode so huge datasets stay constant-RAM.
/// </summary>
public sealed class DuckDbSeedProvider : IMotelySeedProvider, IDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly DbDataReader _reader;
    private readonly Lock _lock = new();
    private bool _disposed;

    public long SeedCount { get; }

    public static bool HandlesPath(string argument) => DuckDbPaths.IsSeedSource(argument);

    /// <summary>
    /// The extension DuckDB should read. A signed remote URL keeps its query string, so the
    /// extension comes from the URI path rather than from the whole address.
    /// </summary>
    public static string SourceExtension(string path) => DuckDbPaths.SourceExtension(path);

    public DuckDbSeedProvider(string path, bool distinct = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = path.Replace('\\', '/');

        _connection = new DuckDBConnection("Data Source=:memory:;threads=1");
        _connection.Open();

        if (IsRemotePath(path))
        {
            using var extCmd = _connection.CreateCommand();
            extCmd.CommandText = "INSTALL httpfs; LOAD httpfs;";
            extCmd.ExecuteNonQuery();
        }

        string ext = SourceExtension(path);
        string from = ext switch
        {
            ".parquet" or ".pq" => $"read_parquet('{EscapeSql(path)}')",
            ".db" or ".duckdb" or ".sqlite" or ".sqlite3" => AttachSeedTable(path),
            _ => $"read_csv('{EscapeSql(path)}', header = false, null_padding = true, all_varchar = true, strict_mode = false, ignore_errors = true)",
        };

        const string seedExpr = "upper(replace(trim(CAST(#1 AS VARCHAR), ' ' || chr(9) || chr(13)), '0', 'O'))";
        string select = distinct ? "SELECT DISTINCT" : "SELECT";
        string count = distinct ? $"COUNT(DISTINCT {seedExpr})" : "COUNT(*)";
        string where = $" WHERE {seedExpr} IS NOT NULL AND {seedExpr} SIMILAR TO '[1-9A-Z]{{0,8}}'";

        using (var countCmd = _connection.CreateCommand())
        {
            countCmd.CommandText = $"SELECT {count} FROM {from}{where}";
            object? scalar = countCmd.ExecuteScalar();
            SeedCount = scalar is null || scalar is DBNull ? -1 : Convert.ToInt64(scalar);
        }

        var cmd = _connection.CreateCommand();
        cmd.CommandText = $"{select} {seedExpr} AS seed FROM {from}{where}";
        cmd.UseStreamingMode = true;
        _reader = cmd.ExecuteReader();
    }

    public IEnumerable<string> EnumerateSeeds()
    {
        while (TryNextSeed(out string seed))
            yield return seed;
    }

    public string NextSeed() => TryNextSeed(out string seed) ? seed : string.Empty;

    /// <summary>
    /// An empty string is a seed the file can hold, not the end of the stream. Callers that need
    /// to tell the two apart use this method; EnumerateSeeds ends only when it returns false.
    /// </summary>
    public bool TryNextSeed(out string seed)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                seed = string.Empty;
                return false;
            }

            while (_reader.Read())
            {
                if (!_reader.IsDBNull(0))
                {
                    seed = _reader.GetString(0);
                    return true;
                }
            }
            seed = string.Empty;
            return false;
        }
    }

    public int NextSeeds(string[] buffer)
    {
        if (buffer is not { Length: > 0 })
            return 0;

        lock (_lock)
        {
            if (_disposed)
                return 0;

            int count = 0;
            while (count < buffer.Length && _reader.Read())
            {
                if (!_reader.IsDBNull(0))
                    buffer[count++] = _reader.GetString(0);
            }
            return count;
        }
    }

    private string AttachSeedTable(string path)
    {
        try
        {
            using var attach = _connection.CreateCommand();
            attach.CommandText = $"ATTACH '{EscapeSql(path)}' AS src (READ_ONLY)";
            attach.ExecuteNonQuery();
        }
        catch (Exception)
        {
            using var sqlite = _connection.CreateCommand();
            sqlite.CommandText =
                $"INSTALL sqlite; LOAD sqlite; ATTACH '{EscapeSql(path)}' AS src (TYPE sqlite, READ_ONLY)";
            sqlite.ExecuteNonQuery();
        }

        var tables = new List<string>();
        using (var q = _connection.CreateCommand())
        {
            q.CommandText = "SELECT table_name FROM duckdb_tables() WHERE database_name = 'src'";
            using var r = q.ExecuteReader();
            while (r.Read())
                tables.Add(r.GetString(0));
        }

        string? table = tables.Find(t => t.Equals("results", StringComparison.OrdinalIgnoreCase));
        table ??= tables.Find(t => t.Equals("seeds", StringComparison.OrdinalIgnoreCase));
        table ??= tables.Count == 1 ? tables[0] : null;
        if (table is null)
            throw new InvalidOperationException(
                $"Database '{path}' needs a 'results' or 'seeds' table, or a single table; found: [{string.Join(", ", tables)}]."
            );

        return $"src.\"{table.Replace("\"", "\"\"")}\"";
    }

    private static string EscapeSql(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    private static bool IsRemotePath(string path) => DuckDbPaths.IsRemote(path);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _reader.Dispose();
        _connection.Dispose();
    }
}
