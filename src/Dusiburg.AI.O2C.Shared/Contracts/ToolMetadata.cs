namespace Dusiburg.AI.O2C.Shared.Contracts;

/// <summary>Chiavi di <c>_meta</c> dei tool MCP del POC, pubblicate in <c>tools/list</c>.</summary>
public static class ToolMetadata
{
    /// <summary>
    /// Tool sensibile, soggetto alla policy di approvazione dell'orchestratore (§6, §7; G2.5): oggi solo <c>create_order</c>.
    /// </summary>
    public const string Sensitive = "o2c.sensitive";
}
