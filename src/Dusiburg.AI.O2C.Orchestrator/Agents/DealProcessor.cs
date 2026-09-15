using System.Diagnostics;
using Dusiburg.AI.O2C.Orchestrator.Telemetry;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Correlation;
using Dusiburg.AI.O2C.Shared.Telemetry;

namespace Dusiburg.AI.O2C.Orchestrator.Agents;

/// <summary>
/// Ingresso del workflow su un deal: genera il correlation id una sola volta, apre lo span radice
/// <c>o2c.process_deal</c> e lo scope di log, poi esegue l'agente.
/// </summary>
public sealed class DealProcessor(SingleOrderAgent agent, ICorrelationContext correlationContext, ILogger<DealProcessor> logger)
{
    public async Task<DealProcessingResult> ProcessAsync(string dealId, CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId.New();

        using var correlationScope = correlationContext.Begin(correlationId);
        using var logScope = logger.BeginScope(new Dictionary<string, object> { [O2CTelemetry.Attributes.CorrelationId] = correlationId });
        using var activity = OrchestratorTelemetry.Source.StartActivity(OrchestratorTelemetry.ProcessDealActivityName);

        activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, correlationId);
        activity?.SetTag(O2CTelemetry.Attributes.DealId, dealId);
        activity?.SetTag(O2CTelemetry.Attributes.AgentName, SingleOrderAgent.AgentName);
        activity?.SetBaggage(O2CTelemetry.Attributes.CorrelationId, correlationId);

        var result = await agent.ProcessAsync(dealId, correlationId, cancellationToken);

        activity?.SetTag(O2CTelemetry.Attributes.DealOutcome, result.Status.ToString());

        if (result.Status != DealStatus.OrderCreated)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Status.ToString());
        }

        logger.LogInformation(
            "Deal {DealId} elaborato con {Provider}/{Model}: {DealOutcome} {ErpOrderNumber} in {ToolCallCount} chiamate a tool",
            dealId, result.Provider, result.Model, result.Status, result.ErpOrderNumber, result.ToolCalls.Count);

        return result;
    }
}
