using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Correlation;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Dusiburg.AI.O2C.Mcp.Hosting;

/// <summary>
/// Filtro su ogni <c>tools/call</c> (2.1): span <c>mcp.tool {tool.name}</c> con <c>tool.name</c>, <c>correlation.id</c> e
/// <c>tool.outcome</c>, log strutturato della chiamata e traduzione delle eccezioni negli errori di §6. Senza filtro l'SDK
/// trasformerebbe ogni eccezione in un testo generico (spike S2): qui nessuna eccezione arriva al client.
/// </summary>
internal static class ToolCallFilter
{
    public const string LoggerCategory = "Dusiburg.AI.O2C.Mcp.ToolCalls";

    private const string InternalErrorMessage = "Errore interno durante l'esecuzione del tool.";

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create(ActivitySource activitySource) =>
        next => async (context, cancellationToken) =>
        {
            var toolName = context.Params?.Name ?? "unknown";
            var correlationId = context.Services?.GetService<ICorrelationContext>()?.Current;
            var logger = context.Services?.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;
            var started = Stopwatch.GetTimestamp();

            using var activity = activitySource.StartActivity($"mcp.tool {toolName}");
            activity?.SetTag(O2CTelemetry.Attributes.ToolName, toolName);
            activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, correlationId);

            CallToolResult result;
            string? errorCode = null;

            try
            {
                result = await next(context, cancellationToken);
            }
            catch (ToolException exception)
            {
                errorCode = exception.Code;
                result = ToolResults.Error(exception.Code, exception.Message);
            }
            catch (McpProtocolException)
            {
                // Tool inesistente e altri errori di protocollo: restano errori JSON-RPC.
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException)
            {
                // Argomenti mancanti o non convertibili (binding dell'SDK) e input rifiutato dal codice del tool.
                errorCode = ToolErrorCodes.ValidationError;
                result = ToolResults.Error(errorCode, exception.Message);
            }
            catch (Exception exception)
            {
                errorCode = ToolErrorCodes.Internal;
                result = ToolResults.Error(errorCode, InternalErrorMessage);

                logger.LogError(exception, "Eccezione non gestita nel tool {ToolName}", toolName);
            }

            var outcome = errorCode is null ? "ok" : $"error:{errorCode}";

            activity?.SetTag(O2CTelemetry.Attributes.ToolOutcome, outcome);

            if (errorCode is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, errorCode);
            }

            logger.Log(
                errorCode is null ? LogLevel.Information : LogLevel.Warning,
                "Tool {ToolName} completato con esito {ToolOutcome} in {ElapsedMs} ms",
                toolName,
                outcome,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return result;
        };
}
