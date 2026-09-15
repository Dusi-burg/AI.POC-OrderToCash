using System.Text.Encodings.Web;
using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Errors;
using ModelContextProtocol.Protocol;

namespace Dusiburg.AI.O2C.Mcp.Hosting;

/// <summary>
/// Risultati MCP di errore (D33, M22): <c>isError = true</c> e l'envelope <c>{ error: { code, message } }</c> come testo JSON,
/// senza <c>structuredContent</c>, che resta riservato ai risultati conformi all'<c>outputSchema</c> del tool.
/// </summary>
public static class ToolResults
{
    // Testo destinato al modello, non a una pagina HTML: le lettere accentate restano leggibili.
    private static readonly JsonSerializerOptions ErrorJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static CallToolResult Error(string code, string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(ToolErrorResponse.Create(code, message), ErrorJson) }]
    };
}
