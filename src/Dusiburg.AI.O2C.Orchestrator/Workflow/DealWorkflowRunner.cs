using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Model;
using Dusiburg.AI.O2C.Orchestrator.Telemetry;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Workflow;

/// <summary>
/// Workflow a tre agenti con handoff di Agent Framework (4.3, D44). Gli agenti chiamano i tool e i trasferimenti;
/// l'host legge il deal, persiste lo stato, termina sui fatti, decide l'esito e scrive sul CRM gli esiti di arresto (G4.1).
/// </summary>
public sealed class DealWorkflowRunner(
    IModelClientFactory models,
    IToolCatalog toolCatalog,
    IWorkflowStateStore stateStore,
    ILogger<DealWorkflowRunner> logger) : IDealAgent
{
    /// <summary>Limite di continuazioni autonome per agente: senza limite un agente che non passa la mano gira a vuoto (spike S4: 104 turni).</summary>
    public const int MaxAutonomousTurns = 3;

    private const string HandoffToolPrefix = "handoff_to";

    public string Mode => AgentModes.Multi;

    /// <summary>Con la rielaborazione consentita il workflow parte sempre, quindi il risultato non è mai nullo.</summary>
    public async Task<DealProcessingResult> ProcessAsync(string dealId, string correlationId, CancellationToken cancellationToken) =>
        (await ProcessAsync(dealId, correlationId, dealRevision: null, reprocess: true, cancellationToken))!;

    /// <summary>
    /// Elabora un deal. <paramref name="dealRevision"/> arriva dall'evento; se manca (CLI) si legge dal CRM.
    /// Con <paramref name="reprocess"/> falso un deal già ricevuto alla stessa revisione non viene rielaborato (<c>null</c>).
    /// </summary>
    public async Task<DealProcessingResult?> ProcessAsync(
        string dealId, string correlationId, int? dealRevision, bool reprocess, CancellationToken cancellationToken)
    {
        var tools = await toolCatalog.GetToolsAsync(cancellationToken);
        var context = new DealRunContext(dealId, correlationId, "Host", new HashSet<string>());
        var revision = dealRevision ?? await ReadRevisionAsync(tools, context, cancellationToken);

        if (!await stateStore.TryStartAsync(correlationId, dealId, revision, reprocess, cancellationToken))
        {
            logger.LogInformation("Deal {DealId} alla revisione {DealRevision} già ricevuto: nessun nuovo workflow", dealId, revision);

            return null;
        }

        using var model = models.Create();

        var agents = WorkflowAgents.All.ToDictionary(a => a.Name, a => CreateAgent(a, model, tools, context));

        var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(agents[WorkflowAgents.Intake.Name])
            .WithHandoff(agents[WorkflowAgents.Intake.Name], agents[WorkflowAgents.Fulfillment.Name], "The deal passed intake validation.")
            .WithHandoff(agents[WorkflowAgents.Fulfillment.Name], agents[WorkflowAgents.Order.Name], "Stock was checked for every line item.")
            .WithAutonomousMode(MaxAutonomousTurns)
            .WithTerminationCondition(_ => context.IsTerminal)
            .Build();

        await RunAsync(workflow, context, cancellationToken);

        var result = await CompleteAsync(context, tools, model, cancellationToken);

        await stateStore.UpdateAsync(correlationId, PhaseOf(result.Status), context.ToStateJson(), cancellationToken);

        return result;
    }

    private async Task RunAsync(Microsoft.Agents.AI.Workflows.Workflow workflow, DealRunContext context, CancellationToken cancellationToken)
    {
        Activity? agentRun = null;
        string? currentAgent = null;
        var seenCalls = new HashSet<string>();

        try
        {
            await using var run = await InProcessExecution.RunStreamingAsync(
                workflow, new List<ChatMessage> { new(ChatRole.User, $"Process CRM deal {context.DealId}.") }, cancellationToken: cancellationToken);

            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            await foreach (var workflowEvent in run.WatchStreamAsync(cancellationToken))
            {
                if (workflowEvent is not AgentResponseUpdateEvent update)
                {
                    continue;
                }

                var agentName = WorkflowAgents.All.FirstOrDefault(a => update.ExecutorId.StartsWith(a.Name, StringComparison.Ordinal))?.Name;

                if (agentName is not null && agentName != currentAgent)
                {
                    agentRun?.Dispose();
                    agentRun = OrchestratorTelemetry.Source.StartActivity("agent.run");
                    agentRun?.SetTag(O2CTelemetry.Attributes.AgentName, agentName);
                    agentRun?.SetTag(O2CTelemetry.Attributes.CorrelationId, context.CorrelationId);
                    currentAgent = agentName;

                    await stateStore.UpdateAsync(context.CorrelationId, PhaseOfAgent(agentName), context.ToStateJson(), cancellationToken);
                }

                foreach (var call in update.Update.Contents.OfType<FunctionCallContent>())
                {
                    if (currentAgent is null || !call.Name.StartsWith(HandoffToolPrefix, StringComparison.Ordinal) || !seenCalls.Add(call.CallId))
                    {
                        continue;
                    }

                    RecordHandoff(context, currentAgent, call);
                }
            }
        }
        finally
        {
            agentRun?.Dispose();
        }
    }

    private void RecordHandoff(DealRunContext context, string from, FunctionCallContent call)
    {
        var to = WorkflowAgents.NextOf(from)?.Name ?? "?";
        var reason = call.Arguments?.TryGetValue("reasonForHandoff", out var value) == true
            ? value is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : value?.ToString()
            : null;

        using var handoff = OrchestratorTelemetry.Source.StartActivity("agent.handoff");
        handoff?.SetTag(O2CTelemetry.Attributes.HandoffFrom, from);
        handoff?.SetTag(O2CTelemetry.Attributes.HandoffTo, to);
        handoff?.SetTag(O2CTelemetry.Attributes.HandoffReason, reason);
        handoff?.SetTag(O2CTelemetry.Attributes.CorrelationId, context.CorrelationId);

        context.RecordHandoff(new HandoffRecord(from, to, reason));

        logger.LogInformation("Handoff {HandoffFrom} → {HandoffTo} per {DealId}: {HandoffReason}", from, to, context.DealId, reason);
    }

    private static AIAgent CreateAgent(AgentDefinition definition, ModelClient model, IReadOnlyList<AgentTool> tools, DealRunContext context)
    {
        var scope = new AgentScope(definition.Name, definition.AllowedTools);

        var agentTools = tools
            .Where(tool => definition.AllowedTools.Contains(tool.QualifiedName))
            .Select(tool => (AITool)new GuardedToolFunction(tool, context, scope))
            .ToList();

        if (definition.StopVerdict is not null)
        {
            agentTools.Add(WorkflowAgents.CreateVerdictTool(definition, context));
        }

        var chatOptions = model.DefaultOptions.Clone();
        chatOptions.Instructions = definition.Instructions;
        chatOptions.Tools = agentTools;

        return new ChatClientAgent(model.ChatClient, new ChatClientAgentOptions
        {
            Id = definition.Name,
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = chatOptions
        });
    }

    /// <summary>Lettura deterministica del deal da parte dell'host: serve la revisione per l'idempotenza prima di avviare gli agenti.</summary>
    private static async Task<int> ReadRevisionAsync(IReadOnlyList<AgentTool> tools, DealRunContext context, CancellationToken cancellationToken)
    {
        var getDeal = tools.Single(t => t.QualifiedName == AgentToolNames.GetDeal).Function;
        var result = ToolResultReader.Read(await getDeal.InvokeAsync(new AIFunctionArguments { ["dealId"] = context.DealId }, cancellationToken));

        return result.Succeeded && result.Data.Deserialize<DealDto>(AgentJson.Options) is { } deal ? deal.Revision : 0;
    }

    /// <summary>Esito dai fatti (come in Fase 3) e scrittura sul CRM degli esiti di arresto da parte dell'host (G4.1).</summary>
    private async Task<DealProcessingResult> CompleteAsync(
        DealRunContext context, IReadOnlyList<AgentTool> tools, ModelClient model, CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        DealStatus status;

        if (context.Order is not null && context.CrmStatus == DealStatus.OrderCreated)
        {
            status = DealStatus.OrderCreated;
        }
        else if (context.Verdict is { Status: DealStatus.Discarded } discarded && IntakeRejects(context.Deal))
        {
            status = DealStatus.Discarded;
            reasons.Add($"{discarded.Agent}: {discarded.Reason}");
        }
        else
        {
            status = DealStatus.Failed;

            if (context.Verdict is { } verdict)
            {
                reasons.Add($"{verdict.Agent}: {verdict.Reason}");
            }

            if (context.Verdict is { Status: DealStatus.Discarded })
            {
                reasons.Add("Verdetto Discarded non confermato dai dati del deal.");
            }

            if (context.Order is null && context.Verdict is null)
            {
                reasons.Add("Nessun ordine creato in ERP.");
            }
        }

        if (status != DealStatus.OrderCreated && context.CrmStatus != status && context.Order is null)
        {
            await WriteCrmOutcomeAsync(tools, context, status, string.Join(" ", reasons), cancellationToken);
        }

        return new DealProcessingResult(
            context.DealId, context.CorrelationId, status, context.Order?.OrderNumber, reasons, null,
            ModelOutcomeValid: context.IsTerminal, model.Provider, model.ModelId, context.ToolCalls)
        {
            Handoffs = context.Handoffs
        };
    }

    /// <summary>Regole di intake verificate dall'host sui fatti del <c>get_deal</c> (§5, D22).</summary>
    internal static bool IntakeRejects(DealDto? deal) =>
        deal is not null
        && (deal.Stage != "ClosedWon"
            || !string.Equals(deal.Currency, "EUR", StringComparison.OrdinalIgnoreCase)
            || deal.LineItems.Count == 0
            || deal.LineItems.Sum(l => l.Quantity * l.UnitPrice) != deal.Amount);

    private async Task WriteCrmOutcomeAsync(
        IReadOnlyList<AgentTool> tools, DealRunContext context, DealStatus status, string note, CancellationToken cancellationToken)
    {
        var scope = new AgentScope("Host", new HashSet<string> { AgentToolNames.UpdateDeal });
        var updateDeal = new GuardedToolFunction(tools.Single(t => t.QualifiedName == AgentToolNames.UpdateDeal), context, scope);

        var arguments = new AIFunctionArguments
        {
            ["dealId"] = context.DealId,
            ["status"] = status.ToString(),
            ["note"] = note.Length > 1000 ? note[..1000] : note
        };

        await updateDeal.InvokeAsync(arguments, cancellationToken);

        logger.LogInformation("Deal {DealId} segnato {DealOutcome} dall'orchestratore: {Note}", context.DealId, status, note);
    }

    private static WorkflowPhase PhaseOfAgent(string agentName) =>
        agentName == WorkflowAgents.Intake.Name ? WorkflowPhase.Intake
        : agentName == WorkflowAgents.Fulfillment.Name ? WorkflowPhase.Fulfillment
        : WorkflowPhase.Order;

    private static WorkflowPhase PhaseOf(DealStatus status) => status switch
    {
        DealStatus.OrderCreated => WorkflowPhase.Completed,
        DealStatus.Discarded => WorkflowPhase.Discarded,
        _ => WorkflowPhase.Failed
    };
}
