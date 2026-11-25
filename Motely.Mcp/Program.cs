using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motely.Mcp;

/// <summary>
/// The stdio MCP server. It speaks MCP on stdout, so nothing here may write to stdout: Motely's
/// search engine is quiet, and logs would corrupt the protocol stream.
/// <para>
/// Tools are registered explicitly (no assembly scanning) and use only Motely's public API — no
/// friend assembly, no reflection into the engine. The descriptions are the bot-facing contract;
/// Motely.Web's WebMCP tools deliberately use the same words and result text.
/// </para>
/// </summary>
internal static class Program
{
    private static async Task Main()
    {
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        Add(tools, MotelyToolNames.SeedValidate, nameof(MotelyTools.SeedValidate), MotelyToolDescriptions.SeedValidate, readOnly: true);
        Add(tools, MotelyToolNames.SeedSearch, nameof(MotelyTools.SeedSearch), MotelyToolDescriptions.SeedSearch, readOnly: true);
        Add(tools, MotelyToolNames.SoulOrder, nameof(MotelyTools.SoulOrder), MotelyToolDescriptions.SoulOrder, readOnly: true);
        Add(tools, MotelyToolNames.BestSeed, nameof(MotelyTools.BestSeed), MotelyToolDescriptions.BestSeed, readOnly: true);
        Add(tools, MotelyToolNames.AnalyzeSeed, nameof(MotelyTools.AnalyzeSeed), MotelyToolDescriptions.AnalyzeSeed, readOnly: true);

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "motely",
                Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            },
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability(),
            },
            ToolCollection = tools,
        };

        await using var server = McpServer.Create(new StdioServerTransport(options), options);
        await server.RunAsync();
    }

    private static void Add(
        McpServerPrimitiveCollection<McpServerTool> tools,
        string name,
        string methodName,
        string description,
        bool readOnly
    )
    {
        var method = typeof(MotelyTools).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new InvalidOperationException($"Missing MCP tool method '{methodName}'.");

        tools.Add(
            McpServerTool.Create(
                method,
                options: new McpServerToolCreateOptions
                {
                    Name = name,
                    Description = description,
                    ReadOnly = readOnly,
                    Destructive = false,
                    Idempotent = true,
                    OpenWorld = false,
                }
            )
        );
    }
}
