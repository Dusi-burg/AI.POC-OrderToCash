using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dusiburg.AI.O2C.Orchestrator.Telemetry;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Shared.Idempotency;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tools;

/// <summary>
/// Guardia attorno a ogni tool (3.3), riusata da tutte le fasi: allow-list dell'agente, parametri di <c>create_order</c>
/// calcolati dal codice (D17), fatti del run e span <c>tool.call</c> con gli attributi di §12.
/// </summary>
public sealed class GuardedToolFunction : DelegatingAIFunction
{
    /// <summary>Argomenti di <c>create_order</c> nascosti al modello e iniettati dal codice.</summary>
    public static readonly IReadOnlyList<string> InjectedCreateOrderArguments = ["idempotencyKey", "externalRef"];

    private readonly AgentTool _tool;
    private readonly DealRunContext _context;
    private readonly JsonElement _schema;

    public GuardedToolFunction(AgentTool tool, DealRunContext context)
        : base(tool.Function)
    {
        _tool = tool;
        _context = context;
        _schema = tool.QualifiedName == AgentToolNames.CreateOrder
            ? WithoutProperties(tool.Function.JsonSchema, InjectedCreateOrderArguments)
            : tool.Function.JsonSchema;
    }

    public string QualifiedName => _tool.QualifiedName;

    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        using var activity = OrchestratorTelemetry.Source.StartActivity($"tool.call {QualifiedName}");
        activity?.SetTag(O2CTelemetry.Attributes.AgentName, _context.AgentName);
        activity?.SetTag(O2CTelemetry.Attributes.ToolName, QualifiedName);
        activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, _context.CorrelationId);

        var started = Stopwatch.GetTimestamp();
        var outcome = $"error:{ToolErrorCodes.Internal}";

        try
        {
            var (result, returnValue) = await InvokeGuardedAsync(arguments, cancellationToken);

            outcome = result.Succeeded ? "ok" : $"error:{result.ErrorCode}";

            return returnValue;
        }
        finally
        {
            activity?.SetTag(O2CTelemetry.Attributes.ToolOutcome, outcome);

            if (outcome != "ok")
            {
                activity?.SetStatus(ActivityStatusCode.Error, outcome);
            }

            _context.RecordCall(new ToolCallRecord(QualifiedName, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        }
    }

    private async Task<(ToolResult Result, object? ReturnValue)> InvokeGuardedAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // Difesa in profondità: il catalogo offre all'agente solo i tool consentiti, ma la guardia non si fida.
        if (!_context.AllowedTools.Contains(QualifiedName))
        {
            return Reject(ToolErrorCodes.Unauthorized, $"Tool '{Name}' is not allowed for agent {_context.AgentName}.");
        }

        if (QualifiedName == AgentToolNames.CreateOrder)
        {
            if (_context.Deal is not { } deal)
            {
                return Reject(ToolErrorCodes.ValidationError, "Call get_deal before create_order.");
            }

            // D17: chiave di idempotenza e riferimento esterno dal codice; i valori eventualmente proposti dal modello si sovrascrivono.
            arguments["idempotencyKey"] = IdempotencyKey.From(_context.DealId, deal.Revision);
            arguments["externalRef"] = _context.DealId;
        }

        var value = await base.InvokeCoreAsync(arguments, cancellationToken);
        var result = ToolResultReader.Read(value);

        if (result.Succeeded)
        {
            _context.RecordFacts(QualifiedName, arguments, result.Data);
        }

        return (result, value);
    }

    private static (ToolResult Result, object? ReturnValue) Reject(string code, string message)
    {
        var envelope = JsonSerializer.SerializeToElement(ToolErrorResponse.Create(code, message), AgentJson.Options);

        return (new ToolResult(false, envelope, code, message), envelope);
    }

    private static JsonElement WithoutProperties(JsonElement schema, IReadOnlyList<string> names)
    {
        var node = JsonNode.Parse(schema.GetRawText())?.AsObject() ?? [];

        if (node["properties"] is JsonObject properties)
        {
            foreach (var name in names)
            {
                properties.Remove(name);
            }
        }

        if (node["required"] is JsonArray required)
        {
            foreach (var item in required.Where(i => i is not null && names.Contains(i.GetValue<string>())).ToList())
            {
                required.Remove(item);
            }
        }

        return JsonSerializer.SerializeToElement(node);
    }
}
