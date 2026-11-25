namespace Motely.Mcp;

/// <summary>
/// Bots launch MCP servers from all sorts of working directories. A relative filter or seed path is
/// resolved from the current directory first, then from the server binary upward (bin/… → project
/// root), so `ColaLit` and `mySeeds.csv` work without an absolute path.
/// </summary>
internal static class McpPathResolver
{
    public static bool TryFindFile(string input, out string path)
    {
        if (File.Exists(input))
        {
            path = input;
            return true;
        }

        if (!Path.IsPathRooted(input))
        {
            string fromCurrentDirectory = Path.Combine(Directory.GetCurrentDirectory(), input);
            if (File.Exists(fromCurrentDirectory))
            {
                path = fromCurrentDirectory;
                return true;
            }

            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, input);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }

            path = fromCurrentDirectory;
            return false;
        }

        path = input;
        return false;
    }
}
