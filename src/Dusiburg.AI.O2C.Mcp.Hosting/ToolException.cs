using Dusiburg.AI.O2C.Shared.Errors;

namespace Dusiburg.AI.O2C.Mcp.Hosting;

/// <summary>
/// Errore di un tool MCP con un codice del catalogo <see cref="ToolErrorCodes"/>: il filtro sulle chiamate lo restituisce
/// come <c>{ error: { code, message } }</c> (§6, D33). Il messaggio arriva al modello: niente dettagli interni.
/// </summary>
public sealed class ToolException : Exception
{
    public ToolException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
