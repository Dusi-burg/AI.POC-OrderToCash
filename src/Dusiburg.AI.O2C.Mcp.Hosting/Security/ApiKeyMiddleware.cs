using System.Security.Cryptography;
using System.Text;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dusiburg.AI.O2C.Mcp.Hosting.Security;

/// <summary>
/// Autenticazione a API key dell'endpoint MCP (G2.3): 401 ProblemDetails con <c>code = UNAUTHORIZED</c>
/// se l'header <c>X-Api-Key</c> è assente o diverso dalla chiave configurata.
/// </summary>
internal sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<McpApiKeyOptions> options, ILogger<ApiKeyMiddleware> logger)
{
    // Si confrontano gli hash, di lunghezza fissa: il tempo del confronto non dipende né dal contenuto né dalla lunghezza della chiave.
    private readonly byte[] _expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ApiKey));

    public async Task InvokeAsync(HttpContext context)
    {
        var provided = context.Request.Headers[O2CMcpServerExtensions.ApiKeyHeaderName].ToString();

        if (provided.Length == 0
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(provided)), _expectedHash))
        {
            logger.LogWarning("Richiesta MCP rifiutata: API key {ApiKeyState}", provided.Length == 0 ? "assente" : "non valida");

            await ToolProblems.Unauthorized("API key assente o non valida.").ExecuteAsync(context);

            return;
        }

        await next(context);
    }
}
