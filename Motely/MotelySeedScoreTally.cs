using System.Runtime.CompilerServices;

namespace Motely.Filters;

public struct MotelyScoredSeedResult : IMotelySeedScores
{
    public const int MAX_TALLY_COUNT = 256;
    private const int InitialTallyCapacity = 4;

    public int Score { get; set; }
    public string Seed { get; set; }
    private int _tallyCount;
    // Most sinks only carry seed and score. Grow on the first tally instead of paying for 256
    // ints on every find: a wide search can report thousands of seeds that never use the space.
    private int[] _tallyValues = Array.Empty<int>();

    public MotelyScoredSeedResult() => Seed = string.Empty;

    public readonly byte[] Tally
    {
        get
        {
            var tally = new byte[_tallyCount];
            for (int i = 0; i < _tallyCount; i++)
                tally[i] = (byte)_tallyValues[i];
            return tally;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(string seed, int score = 0)
    {
        Seed = seed;
        Score = score;
        _tallyCount = 0;
        _tallyValues ??= Array.Empty<int>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddTally(int value)
    {
        _tallyValues ??= Array.Empty<int>();
        if (_tallyCount == _tallyValues.Length)
            GrowBuffer(_tallyCount + 1);
        _tallyValues[_tallyCount++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GrowBuffer(int needed)
    {
        int capacity = _tallyValues.Length switch
        {
            0 => InitialTallyCapacity,
            _ => Math.Min(_tallyValues.Length * 2, MAX_TALLY_COUNT),
        };
        if (capacity < needed)
            capacity = Math.Min(needed, MAX_TALLY_COUNT);
        Array.Resize(ref _tallyValues, capacity);
    }

    public readonly int GetTally(int index)
    {
        if (index < 0 || index >= _tallyCount)
            return 0;
        return _tallyValues[index];
    }

    public readonly int TallyCount
    {
        get { return _tallyCount; }
    }

    public readonly ReadOnlySpan<int> TallyValuesSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tallyValues.AsSpan(0, _tallyCount);
    }

    /// <summary>The tallies as a plain array — a real get/set property so the value crosses
    /// serialization boundaries (WASM interop) intact, backed by the same fixed buffer.</summary>
    public int[] Tallies
    {
        readonly get => _tallyValues.AsSpan(0, _tallyCount).ToArray();
        set
        {
            _tallyValues ??= Array.Empty<int>();
            _tallyCount = Math.Min(value?.Length ?? 0, MAX_TALLY_COUNT);
            if (_tallyCount > _tallyValues.Length)
                GrowBuffer(_tallyCount);
            for (int i = 0; i < _tallyCount; i++)
                _tallyValues[i] = value![i];
        }
    }
}
