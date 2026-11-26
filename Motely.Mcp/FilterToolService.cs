using Motely.Filters;

namespace Motely.Mcp;

/// <summary>A filter ready for a tool call: its config and the name used in result text.</summary>
internal sealed record LoadedFilter(JamlConfig Config, string Label);

/// <summary>
/// Loads a filter from JAML text, a path, or a bare name in Filters/. The last two are a bot
/// convenience; the text contract stays the same whichever form the caller used.
/// </summary>
internal static class FilterToolService
{
    /// <summary>Extensions a bare name is tried with, in order. The repo ships both .yaml and .jaml.</summary>
    private static readonly string[] FilterExtensions = [".yaml", ".yml", ".jaml", ".json"];

    public static bool TryLoad(string? filter, out LoadedFilter? loaded, out string? error)
    {
        loaded = null;
        if (string.IsNullOrWhiteSpace(filter))
        {
            error = "Error: filter is empty — paste the filter's JAML (YAML) text or name one in Filters/.";
            return false;
        }

        string text = filter!.Trim();
        if (!text.Contains('\n') && !text.StartsWith("{", StringComparison.Ordinal))
        {
            bool hasFilterExtension = FilterExtensions.Any(extension =>
                text.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
            bool looksLikePath = hasFilterExtension
                || text.Contains('/')
                || text.Contains('\\');

            if (McpPathResolver.IsOutsideAllowed(text))
            {
                error =
                    $"Error: filter path '{text}' is outside the allowed search areas: "
                    + $"{McpPathResolver.DescribeAllowedRoots()}.";
                return false;
            }

            if (TryResolveFilter(text, out string resolved))
            {
                text = File.ReadAllText(resolved);
            }
            else if (looksLikePath || !text.Contains(':'))
            {
                // Not a file, and not one-line YAML (which carries a ':'), so it was meant as a
                // name or path. Say that plainly instead of handing the name to the YAML parser.
                string expected = hasFilterExtension
                    ? $"'{text}'"
                    : $"a filter named '{text}' in Filters/ ({string.Join(", ", FilterExtensions)})";
                error =
                    $"Error: no filter file found for {expected}. "
                    + "Name one in Filters/ (e.g. \"longCola\") or paste the JAML text.";
                return false;
            }
        }

        if (!JamlConfig.TryLoad(text, out var config, out var loadError))
        {
            error = string.IsNullOrWhiteSpace(loadError)
                ? "Error: the filter did not load."
                : $"Error: the filter did not load: {loadError}";
            return false;
        }

        try
        {
            // CreateSettings is the same "does the search plan build?" check the WebMCP tool uses.
            MotelySearchBuilder.CreateSettings(config);
        }
        catch (Exception ex)
        {
            error = $"Error: the filter did not build a search plan: {ex.GetBaseException().Message}";
            return false;
        }

        loaded = new LoadedFilter(config, config.Name ?? "unnamed");
        error = null;
        return true;
    }

    /// <summary>
    /// Resolves an explicit path as written, or a bare name against each filter extension. A bare
    /// name is tried as <c>Filters/&lt;name&gt;&lt;extension&gt;</c> so <c>Whimsy_Dicetricks314</c>
    /// finds its <c>.jaml</c> as readily as <c>longCola</c> finds its <c>.yaml</c>.
    /// </summary>
    private static bool TryResolveFilter(string text, out string path)
    {
        IEnumerable<string> candidates = FilterExtensions.Any(extension =>
            text.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            ? [text]
            : FilterExtensions.Select(extension => Path.Combine("Filters", text + extension));

        foreach (string candidate in candidates)
        {
            if (McpPathResolver.TryFindFile(candidate, out path))
                return true;
        }

        path = string.Empty;
        return false;
    }
}
