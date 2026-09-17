using Dusiburg.AI.O2C.Shared.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.O2C.ServiceDefaults.Problems;

/// <summary>
/// Errori degli endpoint HTTP come ProblemDetails con l'estensione <c>code</c> del catalogo <see cref="ToolErrorCodes"/>:
/// i server MCP la traducono nell'envelope <c>{ error: { code, message } }</c> (§6).
/// </summary>
public static class ToolProblems
{
    public const string CodeExtension = "code";

    public static ProblemHttpResult Validation(string detail) =>
        Create(StatusCodes.Status400BadRequest, ToolErrorCodes.ValidationError, "Richiesta non valida", detail);

    public static ProblemHttpResult NotFound(string detail) =>
        Create(StatusCodes.Status404NotFound, ToolErrorCodes.NotFound, "Risorsa non trovata", detail);

    public static ProblemHttpResult Conflict(string detail) =>
        Create(StatusCodes.Status409Conflict, ToolErrorCodes.Conflict, "Conflitto", detail);

    public static ProblemHttpResult Unauthorized(string detail) =>
        Create(StatusCodes.Status401Unauthorized, ToolErrorCodes.Unauthorized, "Non autorizzato", detail);

    public static ProblemHttpResult UpstreamUnavailable(string detail) =>
        Create(StatusCodes.Status503ServiceUnavailable, ToolErrorCodes.UpstreamUnavailable, "Servizio a valle non disponibile", detail);

    /// <summary>
    /// Per <c>AddProblemDetails</c>: dà un <c>code</c> anche ai ProblemDetails generati dal framework
    /// (binding non valido, eccezioni non gestite), in base allo status.
    /// </summary>
    public static void Configure(ProblemDetailsOptions options) =>
        options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions.TryAdd(CodeExtension, context.ProblemDetails.Status switch
            {
                StatusCodes.Status400BadRequest => ToolErrorCodes.ValidationError,
                StatusCodes.Status401Unauthorized => ToolErrorCodes.Unauthorized,
                StatusCodes.Status404NotFound => ToolErrorCodes.NotFound,
                StatusCodes.Status409Conflict => ToolErrorCodes.Conflict,
                StatusCodes.Status503ServiceUnavailable => ToolErrorCodes.UpstreamUnavailable,
                _ => ToolErrorCodes.Internal
            });

    /// <summary>
    /// Per <c>UseExceptionHandler</c>: un corpo JSON illeggibile resta un 400. In Development le Minimal API lanciano
    /// <see cref="BadHttpRequestException"/> invece di rispondere, e senza questo selettore diventerebbe un 500.
    /// </summary>
    public static void ConfigureExceptionHandler(ExceptionHandlerOptions options) =>
        options.StatusCodeSelector = exception =>
            exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError;

    private static ProblemHttpResult Create(int statusCode, string code, string title, string detail) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: statusCode,
            title: title,
            extensions: new Dictionary<string, object?> { [CodeExtension] = code });
}
