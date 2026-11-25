using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Motely.Mcp;

/// <summary>
/// One shape for every tool: short human text plus structured JSON. Errors are text-only, start
/// with "Error:", and set isError so a client can branch without parsing the message.
/// </summary>
internal static class ToolResults
{
    public static CallToolResult Ok(string text, object structured) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        StructuredContent = JsonSerializer.SerializeToElement(structured),
        IsError = false,
    };

    public static CallToolResult Error(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = true,
    };
}
