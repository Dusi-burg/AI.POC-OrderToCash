namespace Dusiburg.AI.O2C.Shared.Errors;

public sealed record ToolError(string Code, string Message);

/// <summary>
/// Envelope degli errori dei tool MCP (§6): serializzato come <c>{ "error": { "code", "message" } }</c>.
/// </summary>
public sealed record ToolErrorResponse(ToolError Error)
{
    public static ToolErrorResponse Create(string code, string message) => new(new ToolError(code, message));
}
