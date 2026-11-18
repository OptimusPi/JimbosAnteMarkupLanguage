using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Motely;

// Seeds flow from a SOURCE (the search/provider) to a SINK. Scored seeds are offered to the
// Collector below, which keeps only the top-N by score when given a real limit (push the new
// high score on, evict the lowest off the bottom once the count exceeds it) — or all of them,
// unbounded, when the caller passes int.MaxValue. Survivors get written to their final
// destination — a JAML `seeds:` block on disk.
//
// The pure text rewrite + collector both live here so the CLI (file IO) and Motely.Wasm (browser
// File System Access) share one tested core.
public static class MotelyTopSeedSink
{
    /// <summary>
    /// Bounded top-N-by-score collector. A min-heap keyed on (score, sequence): every offered seed
    /// is enqueued, and once the count exceeds <paramref name="limit"/> the lowest-scoring entry is
    /// dropped. Ties break by insertion order (earlier wins). Not thread-safe; one per search.
    /// </summary>
    public sealed class Collector(int limit)
    {
        private readonly PriorityQueue<SavedSeedEntry, (int Score, long Sequence)> _queue = new();
        private long _sequence;

        // Scored results arrive on every worker thread — the engine invokes result callbacks with
        // no serialization. PriorityQueue is not thread-safe, so concurrent Enqueue can corrupt the
        // heap rather than merely lose an entry, and this queue is what gets written back into the
        // filter's seeds: block. The lock lives here so a caller cannot forget it, and it is only
        // ever taken when a seed actually matches.
        private readonly object _gate = new();

        public void Consider(string seed, int score)
        {
            lock (_gate)
            {
                _queue.Enqueue(new(seed, score, _sequence), (score, _sequence));
                _sequence++;

                if (_queue.Count > limit)
                    _queue.Dequeue();
            }
        }

        public IReadOnlyList<string> GetSeeds()
        {
            lock (_gate)
            {
                return _queue
                    .UnorderedItems.Select(static item => item.Element)
                    .OrderByDescending(static item => item.Score)
                    .ThenBy(static item => item.Sequence)
                    .Select(static item => item.Seed)
                    .Distinct(StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray();
            }
        }
    }

    private readonly record struct SavedSeedEntry(string Seed, int Score, long Sequence);

    /// <summary>
    /// Pure text transform: merge the given seeds into the top-level <c>seeds:</c> block of a
    /// JAML document (appending the block if absent). Seeds already in the document are a seed
    /// provider the user curated — they stay, in front, in their original order; new finds go
    /// after them. No IO, no validation. The original newline style is preserved. Seeds are
    /// normalized (<see cref="MotelyGlobals.NormalizeSeed"/>) and de-duped — no count cap; the
    /// caller's Collector (if any) already decided how many survive.
    /// </summary>
    /// <remarks>
    /// The YAML parser finds the <c>seeds:</c> block: its key and value marks give the exact span to
    /// replace, so everything else in the document, comments included, is kept as written.
    /// </remarks>
    /// <exception cref="YamlException">The document is not valid YAML.</exception>
    public static string RewriteSeedsBlock(string jamlText, IReadOnlyList<string> seeds)
    {
        string newline = jamlText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var text = jamlText.Replace("\r\n", "\n", StringComparison.Ordinal);

        var block = FindSeedsBlock(text);
        var merged = (block?.Existing ?? [])
            .Concat(seeds)
            .Where(static seed => seed is not null && (seed.Length == 0 || !string.IsNullOrWhiteSpace(seed)))
            .Select(static seed => MotelyGlobals.NormalizeSeed(seed))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var replacement = string.Join("\n", BuildSeedsBlockLines(merged));

        string updated;
        if (block is { } found)
        {
            updated = text[..found.Start] + replacement + text[found.End..];
            if (!updated.EndsWith('\n'))
                updated += "\n";
        }
        else
        {
            var lines = text.Split('\n').ToList();
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);
            updated = lines.Count > 0
                ? string.Join("\n", lines) + "\n\n" + replacement + "\n"
                : replacement + "\n";
        }

        return newline == "\n" ? updated : updated.Replace("\n", newline, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rewrite the <c>seeds:</c> block, then confirm the result still loads. Returns false with
    /// <paramref name="error"/> set if the rewritten document does not load, so a bad write is
    /// caught before it ever touches disk.
    /// </summary>
    public static bool TryRewriteAndValidate(
        string jamlText,
        IReadOnlyList<string> seeds,
        out string newText,
        out string? error
    )
    {
        newText = RewriteSeedsBlock(jamlText, seeds);
        if (!Motely.Filters.JamlConfigLoader.TryLoad(newText, out _, out var loadError))
        {
            error = loadError ?? "The updated filter did not load.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// The top-level <c>seeds:</c> entry: the span from its key to the end of the line its value
    /// ends on, and the seeds it already lists in document order. Null if the document has none.
    /// </summary>
    private static SeedsBlock? FindSeedsBlock(string text)
    {
        var parser = new Parser(new System.IO.StringReader(text));
        parser.Consume<StreamStart>();
        if (!parser.TryConsume<DocumentStart>(out _) || !parser.TryConsume<MappingStart>(out _))
            return null;

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>();
            if (!string.Equals(key.Value, "seeds", StringComparison.OrdinalIgnoreCase))
            {
                parser.SkipThisAndNestedEvents();
                continue;
            }

            var existing = new List<string>();
            long valueEnd;
            if (parser.TryConsume<Scalar>(out var scalar))
            {
                AddSeed(existing, scalar);
                valueEnd = scalar.End.Index;
            }
            else
            {
                var sequence = parser.Consume<SequenceStart>();
                valueEnd = sequence.End.Index;
                SequenceEnd? close;
                while (!parser.TryConsume<SequenceEnd>(out close))
                {
                    var item = parser.Consume<Scalar>();
                    AddSeed(existing, item);
                    valueEnd = item.End.Index;
                }
                // A flow list ends at its `]`; a block list ends at its last item (its SequenceEnd
                // mark sits on the next key).
                if (sequence.Style == SequenceStyle.Flow)
                    valueEnd = close!.End.Index;
            }

            // The span runs to the end of the line the value ends on. A block scalar (`- |-`) ends
            // at the start of the next line, so that mark already is the line end.
            int end = (int)valueEnd;
            int lineEnd = end > 0 && text[end - 1] == '\n' ? end - 1 : text.IndexOf('\n', end);
            return new SeedsBlock((int)key.Start.Index, lineEnd < 0 ? text.Length : lineEnd, existing);
        }

        return null;
    }

    /// <summary>
    /// A listed seed: a quoted empty string is the empty seed; YAML's null (unquoted <c>~</c>,
    /// <c>null</c>, or nothing) is no seed.
    /// </summary>
    private static void AddSeed(List<string> seeds, Scalar scalar)
    {
        if (scalar.Style == ScalarStyle.Plain && scalar.Value is "" or "~" or "null" or "Null" or "NULL")
            return;
        seeds.Add(scalar.Value);
    }

    private sealed record SeedsBlock(int Start, int End, List<string> Existing);

    private static List<string> BuildSeedsBlockLines(IReadOnlyList<string> seeds)
    {
        if (seeds.Count == 0)
            return ["seeds: []"];

        var lines = new List<string>(seeds.Count + 1) { "seeds:" };
        // Unquoted, the seed NULL is YAML's null and the file would no longer load.
        lines.AddRange(seeds.Select(static seed => seed is "" or "NULL" ? $"  - \"{seed}\"" : $"  - {seed}"));
        return lines;
    }
}
