using System.Collections.Frozen;
using System.ComponentModel;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Agents;

/// <summary>Un agente del workflow (4.1): istruzioni, tool MCP consentiti e verdetto di arresto disponibile.</summary>
public sealed record AgentDefinition(string Name, string Description, string Instructions, IReadOnlySet<string> AllowedTools, DealStatus? StopVerdict);

/// <summary>
/// I tre agenti di §5 e la loro topologia a soli archi in avanti: Intake → Fulfillment → Order.
/// <para>
/// Nome, descrizione, istruzioni e condizione di handoff vengono dalle specifiche in <c>Agents/Specs</c> (D63);
/// qui restano l'allow-list dei tool, il verdetto di arresto e la topologia, che sono guardrail e non testo.
/// </para>
/// </summary>
public static class WorkflowAgents
{
    public const string ReportDiscardedTool = "report_discarded";

    public const string ReportFailedTool = "report_failed";

    private static readonly AgentSpec IntakeSpec = AgentSpec.Load("IntakeAgent");

    private static readonly AgentSpec FulfillmentSpec = AgentSpec.Load("FulfillmentAgent");

    private static readonly AgentSpec OrderSpec = AgentSpec.Load("OrderAgent");

    /// <summary>Descrizione del tool di handoff di <c>IntakeAgent</c>: il modello la vede dal primo turno.</summary>
    public static string IntakeHandoffCondition { get; } = IntakeSpec.Section(AgentSpec.HandoffSection);

    /// <summary>Descrizione del tool di handoff di <c>FulfillmentAgent</c>.</summary>
    public static string FulfillmentHandoffCondition { get; } = FulfillmentSpec.Section(AgentSpec.HandoffSection);

    public static AgentDefinition Intake { get; } = new(
        IntakeSpec.Name,
        IntakeSpec.Description,
        IntakeSpec.Instructions,
        new[] { AgentToolNames.GetDeal, AgentToolNames.GetCompany }.ToFrozenSet(),
        DealStatus.Discarded);

    public static AgentDefinition Fulfillment { get; } = new(
        FulfillmentSpec.Name,
        FulfillmentSpec.Description,
        FulfillmentSpec.Instructions,
        new[] { AgentToolNames.CheckStock }.ToFrozenSet(),
        DealStatus.Failed);

    public static AgentDefinition Order { get; } = new(
        OrderSpec.Name,
        OrderSpec.Description,
        OrderSpec.Instructions,
        new[] { AgentToolNames.GetCustomer, AgentToolNames.CreateCustomer, AgentToolNames.CreateOrder, AgentToolNames.UpdateDeal }.ToFrozenSet(),
        null);

    public static IReadOnlyList<AgentDefinition> All { get; } = [Intake, Fulfillment, Order];

    public static IReadOnlySet<string> Names { get; } = All.Select(a => a.Name).ToFrozenSet();

    /// <summary>Unico successore di ogni agente (nessun arco di ritorno).</summary>
    public static AgentDefinition? NextOf(string agentName) =>
        agentName == Intake.Name ? Fulfillment : agentName == Fulfillment.Name ? Order : null;

    /// <summary>Tool locale dell'host che registra il verdetto di arresto nel contesto (non è un tool MCP).</summary>
    public static AIFunction CreateVerdictTool(AgentDefinition agent, DealRunContext context)
    {
        var status = agent.StopVerdict ?? throw new InvalidOperationException($"{agent.Name} non ha un verdetto di arresto.");
        var name = status == DealStatus.Discarded ? ReportDiscardedTool : ReportFailedTool;

        return AIFunctionFactory.Create(
            ([Description("Short reason for stopping the workflow.")] string reason) =>
            {
                context.RecordVerdict(new AgentVerdict(agent.Name, status, reason));

                return $"Recorded: the deal will be marked {status}. Stop now.";
            },
            name,
            status == DealStatus.Discarded
                ? "Stops the workflow because the deal is not valid (it will be marked Discarded)."
                : "Stops the workflow because the order cannot be fulfilled (it will be marked Failed).");
    }
}
