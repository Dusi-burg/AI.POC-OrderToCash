using System.Diagnostics;
using Dusiburg.AI.O2C.Orchestrator.Telemetry;
using Dusiburg.AI.O2C.Orchestrator.Workflow;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Correlation;
using Dusiburg.AI.O2C.Shared.Telemetry;

namespace Dusiburg.AI.O2C.Orchestrator.Agents;

/// <summary>
/// Ingresso del workflow su un deal (CLI e, dalla Fase 4, evento <c>deal-closed-won</c>): correlation id, span radice
/// <c>o2c.process_deal</c>, scope di log, scelta fra workflow a tre agenti e agente singolo (D47).
/// </summary>
public sealed class DealProcessor(
    DealWorkflowRunner workflowRunner,
    SingleOrderAgent singleAgent,
    ICorrelationContext correlationContext,
    IConfiguration configuration,
    ILogger<DealProcessor> logger)
{
    /// <summary>Da riga di comando: nuovo correlation id e rielaborazione consentita.</summary>
    public async Task<DealProcessingResult> ProcessAsync(string dealId, CancellationToken cancellationToken) =>
        (await ProcessAsync(dealId, CorrelationId.New(), dealRevision: null, reprocess: true, parentContext: default, cancellationToken))!;

    /// <summary>
    /// Elabora un deal. Da evento: correlation id e revisione del messaggio, nessuna rielaborazione di un deal già ricevuto
    /// (risultato <c>null</c>), traccia collegata al publisher tramite <paramref name="parentContext"/>.
    /// </summary>
    public async Task<DealProcessingResult?> ProcessAsync(
        string dealId, string correlationId, int? dealRevision, bool reprocess, ActivityContext parentContext, CancellationToken cancellationToken)
    {
        var mode = AgentModes.FromConfiguration(configuration);

        using var correlationScope = correlationContext.Begin(correlationId);
        using var logScope = logger.BeginScope(new Dictionary<string, object> { [O2CTelemetry.Attributes.CorrelationId] = correlationId });
        using var activity = OrchestratorTelemetry.Source.StartActivity(OrchestratorTelemetry.ProcessDealActivityName, ActivityKind.Internal, parentContext);

        activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, correlationId);
        activity?.SetTag(O2CTelemetry.Attributes.DealId, dealId);
        activity?.SetTag("o2c.agent_mode", mode);
        activity?.SetBaggage(O2CTelemetry.Attributes.CorrelationId, correlationId);

        var result = mode == AgentModes.Single
            ? await singleAgent.ProcessAsync(dealId, correlationId, cancellationToken)
            : await workflowRunner.ProcessAsync(dealId, correlationId, dealRevision, reprocess, cancellationToken);

        if (result is null)
        {
            activity?.SetTag(O2CTelemetry.Attributes.DealOutcome, "duplicate");

            return null;
        }

        activity?.SetTag(O2CTelemetry.Attributes.DealOutcome, result.Status.ToString());

        if (result.Status != DealStatus.OrderCreated)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Status.ToString());
        }

        logger.LogInformation(
            "Deal {DealId} elaborato ({AgentMode}) con {Provider}/{Model}: {DealOutcome} {ErpOrderNumber}, {ToolCallCount} chiamate a tool, {HandoffCount} handoff",
            dealId, mode, result.Provider, result.Model, result.Status, result.ErpOrderNumber, result.ToolCalls.Count, result.Handoffs.Count);

        return result;
    }
}
