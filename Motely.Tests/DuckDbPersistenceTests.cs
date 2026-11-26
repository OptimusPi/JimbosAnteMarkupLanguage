using DuckDB.NET.Data;
using Motely.Analysis;
using Motely.CLI;
using Motely.Filters;

namespace Motely.Tests;

public sealed class DuckDbPersistenceTests
{
    [Fact]
    public void DuckDbResultSink_WritesDuckDbAndReadsBackViaProviderAndTopN()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"motely-duck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string dbPath = Path.Combine(tempDir, "finds.duckdb");
            using (var sink = new DuckDbResultSink(dbPath, tallyCount: 2, includeWindow: false))
            {
                var r1 = new MotelyScoredSeedResult();
                r1.Reset("ALEEB", 120);
                r1.AddTally(1);
                r1.AddTally(3);
                sink.OnScored(in r1);

                var r2 = new MotelyScoredSeedResult();
                r2.Reset("MOTELY77", 500);
                r2.AddTally(2);
                r2.AddTally(1);
                sink.OnScored(in r2);

                var r3 = new MotelyScoredSeedResult();
                r3.Reset("11111111", 50);
                r3.AddTally(0);
                r3.AddTally(1);
                sink.OnScored(in r3);

                sink.Flush();
                var top2 = sink.GetTopSeeds(2);
                Assert.Equal(["MOTELY77", "ALEEB"], top2);
            }

            using var provider = new DuckDbSeedProvider(dbPath);
            Assert.Equal(3, provider.SeedCount);
            var buf = new string[8];
            int read = provider.NextSeeds(buf);
            Assert.Equal(3, read);
            Assert.Equal(
                new HashSet<string>(["ALEEB", "MOTELY77", "11111111"]),
                new HashSet<string>(buf[..read])
            );
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DuckDbSeedProvider_KeepsAnEmptySeedInsideTheStream()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"motely-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string dbPath = Path.Combine(tempDir, "seeds.duckdb");
            using (var sink = new DuckDbResultSink(dbPath, tallyCount: 0))
            {
                sink.OnSeed("ALEEB");
                sink.OnSeed("");
                sink.OnSeed("MOTELY77");
            }

            using var provider = new DuckDbSeedProvider(dbPath);
            Assert.Equal(3, provider.SeedCount);
            string[] read = provider.EnumerateSeeds().ToArray();
            Assert.Equal(
                new HashSet<string>(["ALEEB", "", "MOTELY77"]),
                new HashSet<string>(read)
            );
            Assert.Contains(string.Empty, read);
            Assert.False(provider.TryNextSeed(out _));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DuckDbSeedProvider_ReadsTheExtensionBeforeASignedUrlQuery()
    {
        const string url = "https://example.com/finds.parquet?sig=abc123&x=1";
        Assert.True(DuckDbSeedProvider.HandlesPath(url));
        Assert.Equal(".parquet", DuckDbSeedProvider.SourceExtension(url));
        Assert.Equal(".pq", DuckDbSeedProvider.SourceExtension("s3://bucket/finds.pq?sig=abc"));
    }

    [Fact]
    public void DuckDbResultSink_ExportsZstdParquetWithJamlyzerWindowAndReadsBack()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"motely-pq-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string pqPath = Path.Combine(tempDir, "perkeo.parquet");
            var config = JamlConfig.FromJaml(
                """
                deck: Red
                stake: White
                must:
                  - boss: TheHook
                    antes: [1]
                should:
                  - rareJoker: Blueprint
                    antes: [1, 2]
                    score: 100
                """
            );
            var plan = MotelySearchBuilder.CreatePlan(config, int.MinValue);

            using (var sink = new DuckDbResultSink(pqPath, plan.ScoreTallyColumnCount, includeWindow: true))
            {
                string[] seeds = ["ALEEB", "MOTELY77", "5X5", "616", "696", "6J6", "7H7", "11111111"];
                using var search = plan
                    .Settings.WithSeedList(seeds)
                    .WithThreadCount(1)
                    .WithQuietMode(true)
                    .WithBatchBoundaryCallback(sink.FlushBatch)
                    .WithSeedAnalyzeProvider(
                        MotelyJamlyzer.CreateRiderDesc(config, sink.OnAnalyzed, eventRolls: 0)
                    )
                    .Start();
                search.AwaitCompletion();
                Assert.False(File.Exists(pqPath));
                sink.Flush();
            }

            Assert.True(File.Exists(pqPath));

            // Verify schema (seed, score, tally1, window) directly with DuckDB read_parquet.
            using var conn = new DuckDBConnection("DataSource=:memory:");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT seed, score, tally1, jamlyzer FROM read_parquet('{pqPath.Replace('\\', '/')}')";
            using var reader = cmd.ExecuteReader();
            int rowCount = 0;
            while (reader.Read())
            {
                rowCount++;
                Assert.False(string.IsNullOrEmpty(reader.GetString(0)));
                string windowJson = reader.GetString(3);
                Assert.Contains("\"antes\"", windowJson, StringComparison.Ordinal);
            }

            // Stream the same Parquet file back through DuckDbSeedProvider.
            using var provider = new DuckDbSeedProvider(pqPath);
            Assert.Equal(rowCount, provider.SeedCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
