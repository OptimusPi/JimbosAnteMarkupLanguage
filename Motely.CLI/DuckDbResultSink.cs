using System.Text;
using System.Text.Json;
using DuckDB.NET.Data;
using Motely.Analysis;
using Motely.Enums;
using Motely.Filters;

namespace Motely.CLI;

/// <summary>
/// DuckDB-backed result sink: streams (seed, score, tally1..tallyN[, window]) via DuckDBAppender
/// with zero C# heap accumulation. Supports a persistent .duckdb file (CHECKPOINT on Flush) or
/// direct ZSTD .parquet export (backed by DuckDB with disk spill if huge).
/// </summary>
internal sealed class DuckDbResultSink : IMotelyResultSink
{
    private readonly Lock _gate = new();
    private readonly DuckDBConnection _connection;
    private readonly int _tallyCount;
    private readonly bool _includeWindow;
    private readonly string? _parquetPath;
    private readonly bool _isFileDb;
    private DuckDBAppender? _appender;
    private readonly Dictionary<string, string>? _pendingWindows;
    private long _bufferedRows;
    private bool _disposed;

    public string OutputPath { get; }

    public DuckDbResultSink(string outputPath, int tallyCount, bool includeWindow = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        OutputPath = Path.GetFullPath(outputPath);
        _tallyCount = Math.Max(0, tallyCount);
        _includeWindow = includeWindow;

        string? dir = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string ext = Path.GetExtension(OutputPath).ToLowerInvariant();
        if (ext is ".parquet" or ".pq")
        {
            _parquetPath = OutputPath;
            _isFileDb = false;
            _connection = new DuckDBConnection("DataSource=:memory:");
        }
        else
        {
            _parquetPath = null;
            _isFileDb = true;
            _connection = new DuckDBConnection($"DataSource={OutputPath}");
        }

        _connection.Open();

        if (!_isFileDb && !string.IsNullOrEmpty(dir))
        {
            string tempDir = Path.Combine(dir, ".motely-duckdb-tmp");
            using var tmpCmd = _connection.CreateCommand();
            tmpCmd.CommandText = $"SET temp_directory='{EscapeSql(tempDir)}'";
            tmpCmd.ExecuteNonQuery();
        }

        EnsureTable();

        if (_includeWindow)
            _pendingWindows = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private void EnsureTable()
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE IF NOT EXISTS results (seed VARCHAR, score INTEGER");
        for (int i = 1; i <= _tallyCount; i++)
            sb.Append($", tally{i} INTEGER");
        if (_includeWindow)
            sb.Append(", jamlyzer VARCHAR");
        sb.Append(')');

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.ExecuteNonQuery();
    }

    public void OnSeed(string seed)
    {
        var row = new MotelyScoredSeedResult();
        row.Reset(seed, 0);
        OnScored(in row);
    }

    public void OnScored(in MotelyScoredSeedResult tally)
    {
        if (_includeWindow)
            return; // Written when OnAnalyzed arrives on the same thread right after scoring.

        lock (_gate)
        {
            if (_disposed)
                return;
            AppendRowLocked(tally.Seed, tally.Score, tally.TallyValuesSpan, windowJson: null);
        }
    }

    public void OnAnalyzed(MotelyJamlyzerSeedResult analyzed)
    {
        string json = SerializeWindow(analyzed);
        lock (_gate)
        {
            if (_disposed)
                return;
            ReadOnlySpan<int> tallies = analyzed.Tally is { } arr ? arr : ReadOnlySpan<int>.Empty;
            AppendRowLocked(analyzed.Seed, analyzed.Score, tallies, json);
        }
    }

    private void AppendRowLocked(string seed, int score, ReadOnlySpan<int> tallies, string? windowJson)
    {
        _appender ??= _connection.CreateAppender("results");
        var row = _appender.CreateRow();
        row.AppendValue(seed);
        row.AppendValue(score);
        for (int i = 0; i < _tallyCount; i++)
            row.AppendValue(i < tallies.Length ? tallies[i] : 0);
        if (_includeWindow)
            row.AppendValue(windowJson ?? "");
        row.EndRow();

        _bufferedRows++;
        if (_bufferedRows >= 4096)
            FlushAppenderLocked();
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            FlushAppenderLocked();

            if (_isFileDb)
            {
                using var cp = _connection.CreateCommand();
                cp.CommandText = "CHECKPOINT";
                cp.ExecuteNonQuery();
            }
            else if (_parquetPath is not null)
            {
                ExportParquetLocked(_parquetPath);
            }
        }
    }

    public void ExportParquet(string parquetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parquetPath);
        string full = Path.GetFullPath(parquetPath);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        lock (_gate)
        {
            if (_disposed)
                return;
            FlushAppenderLocked();
            ExportParquetLocked(full);
        }
    }

    public IReadOnlyList<string> GetTopSeeds(int limit)
    {
        lock (_gate)
        {
            if (_disposed)
                return [];

            FlushAppenderLocked();

            using var cmd = _connection.CreateCommand();
            string limitClause = limit > 0 && limit < int.MaxValue ? $" LIMIT {limit}" : "";
            cmd.CommandText =
                "SELECT seed FROM (SELECT seed, MAX(score) AS score FROM results GROUP BY seed) "
                + $"ORDER BY score DESC, seed ASC{limitClause}";

            var seeds = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                seeds.Add(reader.GetString(0));
            return seeds;
        }
    }

    private void FlushAppenderLocked()
    {
        if (_appender is null)
            return;
        _appender.Close();
        _appender.Dispose();
        _appender = null;
        _bufferedRows = 0;
    }

    private void ExportParquetLocked(string parquetPath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            $"COPY (SELECT * FROM results ORDER BY score DESC, seed ASC) "
            + $"TO '{EscapeSql(parquetPath)}' (FORMAT PARQUET, COMPRESSION ZSTD)";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// NativeAOT-safe compact JSON for the filter's scoped Jamlyzer window (zero reflection).
    /// </summary>
    internal static string SerializeWindow(MotelyJamlyzerSeedResult r)
    {
        using var ms = new MemoryStream(512);
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            if (r.LegendaryOrder.Count > 0)
            {
                w.WriteStartArray("legendaries");
                for (int i = 0; i < r.LegendaryOrder.Count; i++)
                {
                    var leg = r.LegendaryOrder[i];
                    w.WriteStartObject();
                    w.WriteNumber("ante", leg.Ante);
                    w.WriteNumber("slot", leg.PackSlot);
                    w.WriteString("joker", leg.Joker.ToString());
                    if (leg.Edition != MotelyItemEdition.None)
                        w.WriteString("edition", leg.Edition.ToString());
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            w.WriteStartArray("antes");
            for (int i = 0; i < r.Antes.Count; i++)
            {
                var a = r.Antes[i];
                w.WriteStartObject();
                w.WriteNumber("ante", a.Ante);
                w.WriteString("boss", a.Boss.ToString());
                w.WriteString("voucher", a.Voucher.ToString());
                w.WriteString("smallTag", a.SmallBlindTag.ToString());
                w.WriteString("bigTag", a.BigBlindTag.ToString());

                w.WriteStartArray("shop");
                for (int s = 0; s < a.ShopItems.Count; s++)
                    w.WriteStringValue(FormatUtils.FormatItem(a.ShopItems[s]));
                w.WriteEndArray();

                w.WriteStartArray("packs");
                for (int p = 0; p < a.Packs.Count; p++)
                {
                    var pk = a.Packs[p];
                    w.WriteStartObject();
                    w.WriteString("pack", pk.Pack.ToString());
                    w.WriteStartArray("items");
                    for (int c = 0; c < pk.Items.Count; c++)
                        w.WriteStringValue(FormatUtils.FormatItem(pk.Items[c]));
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static string EscapeSql(string path) =>
        path.Replace('\\', '/').Replace("'", "''", StringComparison.Ordinal);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            try
            {
                FlushAppenderLocked();
                if (_isFileDb)
                {
                    using var cp = _connection.CreateCommand();
                    cp.CommandText = "CHECKPOINT";
                    cp.ExecuteNonQuery();
                }
                else if (_parquetPath is not null)
                {
                    ExportParquetLocked(_parquetPath);
                }
            }
            finally
            {
                _disposed = true;
                _pendingWindows?.Clear();
                _connection.Dispose();
            }
        }
    }
}
