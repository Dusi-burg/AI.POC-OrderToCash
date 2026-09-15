namespace Dusiburg.AI.O2C.Mcp.Hosting.Security;

public sealed class McpApiKeyOptions
{
    /// <summary>Chiave attesa nell'header <c>X-Api-Key</c>, letta dalla variabile passata dall'AppHost (es. <c>ERP_MCP_API_KEY</c>).</summary>
    public string ApiKey { get; set; } = string.Empty;
}
