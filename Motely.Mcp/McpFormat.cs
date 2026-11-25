using System.Globalization;

namespace Motely.Mcp;

/// <summary>The same number and duration text WebMCP uses, so both surfaces read identically.</summary>
internal static class McpFormat
{
    // Invariant formats numbers like en-US (comma groups, dot decimal) and works in trimmed,
    // globalization-invariant hosts. GetCultureInfo("en-US") throws there, which broke best_seed.
    private static readonly CultureInfo FormatCulture = CultureInfo.InvariantCulture;

    public static string Number(long value) => value.ToString("N0", FormatCulture);

    public static string Duration(long ms)
    {
        if (ms < 1000)
            return $"{Math.Max(0, ms)}ms";

        double totalHours = ms / 3_600_000.0;
        if (totalHours >= 24)
        {
            int days = (int)Math.Floor(totalHours / 24);
            int hours = (int)Math.Floor(totalHours % 24);
            return $"{days} {(days == 1 ? "day" : "days")} {hours} {(hours == 1 ? "hour" : "hours")}";
        }

        long hoursPart = ms / 3_600_000;
        long minutesPart = ms % 3_600_000 / 60_000;
        double secondsPart = ms % 60_000 / 1000.0;
        return
            $"{hoursPart:00}:{minutesPart:00}:{Math.Floor(secondsPart):00}.{Math.Floor((secondsPart % 1) * 10)}";
    }

    public static string JoinArrows(IEnumerable<string> values)
    {
        string[] array = values.ToArray();
        return array.Length == 0 ? "none" : string.Join(" → ", array);
    }
}
