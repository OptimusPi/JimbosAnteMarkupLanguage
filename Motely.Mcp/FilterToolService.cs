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
            string candidate = text.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                || text.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                || text.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? text
                : Path.Combine("Filters", text + ".yaml");
            if (McpPathResolver.TryFindFile(candidate, out string resolved))
                text = File.ReadAllText(resolved);
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
}
