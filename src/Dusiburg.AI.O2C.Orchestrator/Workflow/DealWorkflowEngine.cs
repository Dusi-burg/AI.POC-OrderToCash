using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Orchestrator.Governance;
using Dusiburg.AI.O2C.Orchestrator.Model;
using Dusiburg.AI.O2C.Orchestrator.Telemetry;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using AgentWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace Dusiburg.AI.O2C.Orchestrator.Workflow;

/// <summary>Workflow pronto per un run, con il modello e i tool che gli servono per tutta la sua durata.</summary>
public sealed record WorkflowRunSetup(ModelClient Model, IReadOnlyList<AgentTool> Tools, AgentWorkflow Workflow) : IDisposable
{
    public void Dispose() => Model.Dispose();
}

/// <summary>
/// Esito del consumo degli eventi di un run: se il workflow si è fermato in attesa di approvazione, la proposta
/// congelata e i motivi che l'hanno richiesta.
/// </summary>
public sealed record WorkflowPumpResult(ApprovalPayload? Suspended, IReadOnlyList<ApprovalReason> Reasons)
{
    public static WorkflowPumpResult Ran { get; } = new(null, []);
}

/// <summary>Decisione umana già presa, consegnata al workflow quando riprende dal checkpoint (5.5).</summary>
public sealed record ApprovalAnswer(bool Approved, string? Reason);

/// <summary>
/// Parte comune fra l'avvio di un workflow su un deal (4.3) e la sua ripresa dopo un'approvazione (5.5): costruzione
/// degli agenti, consumo degli eventi con gli span di §12, guardia sulle richieste di approvazione ed esito dai fatti.
/// </summary>
public sealed class DealWorkflowEngine(
    IModelClientFactory models,
    IToolCatalog toolCatalog,
    ApprovalGate approvalGate,
    IWorkflowStateStore stateStore,
    ILogger<DealWorkflowEngine> logger)
{
    /// <summary>Limite di continuazioni autonome per agente: senza limite un agente che non passa la mano gira a vuoto (spike S4: 104 turni).</summary>
    public const int MaxAutonomousTurns = 3;

    private const string HandoffToolPrefix = "handoff_to";

    public async Task<IReadOnlyList<AgentTool>> GetToolsAsync(CancellationToken cancellationToken) =>
        await toolCatalog.GetToolsAsync(cancellationToken);

    /// <summary>Costruisce il workflow a tre agenti con handoff sul contesto indicato; il chiamante lo esegue o lo riprende.</summary>
    public async Task<WorkflowRunSetup> BuildAsync(DealRunContext context, CancellationToken cancellationToken)
    {
        var tools = await toolCatalog.GetToolsAsync(cancellationToken);
        var model = models.Create();

        var agents = WorkflowAgents.All.ToDictionary(a => a.Name, a => CreateAgent(a, model, tools, context));

        var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(agents[WorkflowAgents.Intake.Name])
            .WithHandoff(agents[WorkflowAgents.Intake.Name], agents[WorkflowAgents.Fulfillment.Name], WorkflowAgents.IntakeHandoffCondition)
            .WithHandoff(agents[WorkflowAgents.Fulfillment.Name], agents[WorkflowAgents.Order.Name], WorkflowAgents.FulfillmentHandoffCondition)
            .WithAutonomousMode(MaxAutonomousTurns)
            .WithTerminationCondition(_ => context.IsTerminal)
            .Build();

        return new WorkflowRunSetup(model, tools, workflow);
    }

    /// <summary>
    /// Consuma gli eventi del run fino alla sua fine: span per agente, handoff dai tool di trasferimento e — quando il
    /// framework espone una richiesta di approvazione — decisione della policy, che approva subito o ferma il workflow.
    /// </summary>
    public async Task<WorkflowPumpResult> PumpAsync(
        StreamingRun run, DealRunContext context, IReadOnlyList<AgentTool> tools, CancellationToken cancellationToken, ApprovalAnswer? answer = null)
    {
        var tracker = new AgentTracker();
        var result = WorkflowPumpResult.Ran;

        try
        {
            await foreach (var workflowEvent in run.WatchStreamAsync(cancellationToken))
            {
                switch (workflowEvent)
                {
                    case RequestInfoEvent request when result.Suspended is null:
                        result = await HandleApprovalRequestAsync(run, request.Request, context, tools, answer, cancellationToken);
                        break;

                    // Lo stream di un run con richieste pendenti, o già arrivato ai fatti che chiudono il workflow, resta
                    // aperto in attesa di un'altra interazione: smettere di consumarlo al termine del superstep è ciò
                    // che permette al chiamante di andare avanti. Il superstep è committato, quindi il checkpoint c'è.
                    case SuperStepCompletedEvent when result.Suspended is not null || context.IsTerminal:
                        return result;

                    case AgentResponseUpdateEvent update:
                        await TrackAgentAsync(update, context, tracker, cancellationToken);
                        break;
                }
            }
        }
        finally
        {
            tracker.Dispose();
        }

        return result;
    }

    /// <summary>Agente corrente del run e span aperto su di lui: vive quanto il consumo degli eventi.</summary>
    private sealed class AgentTracker : IDisposable
    {
        public Activity? Span { get; set; }

        public string? AgentName { get; set; }

        public HashSet<string> SeenCalls { get; } = [];

        public void Dispose() => Span?.Dispose();
    }

    /// <summary>
    /// Risponde alla richiesta esposta dal framework: approvazione immediata oppure sospensione del workflow (5.2).
    /// Alla ripresa <paramref name="answer"/> porta la decisione umana già presa e la policy non viene rivalutata (5.5).
    /// </summary>
    private async Task<WorkflowPumpResult> HandleApprovalRequestAsync(
        StreamingRun run,
        ExternalRequest request,
        DealRunContext context,
        IReadOnlyList<AgentTool> tools,
        ApprovalAnswer? answer,
        CancellationToken cancellationToken)
    {
        if (request.Data.As<ToolApprovalRequestContent>() is not { } approval)
        {
            logger.LogWarning("Richiesta esterna non riconosciuta sulla porta {PortId}: ignorata", request.PortInfo?.PortId);

            return WorkflowPumpResult.Ran;
        }

        if (answer is { } decided)
        {
            await run.SendResponseAsync(request.CreateResponse(approval.CreateResponse(decided.Approved, decided.Reason)));

            return WorkflowPumpResult.Ran;
        }

        var verdict = await approvalGate.EvaluateAsync(approval, context, tools, cancellationToken);

        if (verdict.Stop is { } stop)
        {
            // Arresto deciso dall'host (D61): il verdetto rende il workflow terminale e la chiamata si rifiuta, così
            // create_order non parte. L'esito Failed lo scrive CompleteAsync, come per gli altri arresti.
            context.RecordVerdict(stop);

            await run.SendResponseAsync(request.CreateResponse(approval.CreateResponse(approved: false, stop.Reason)));

            return WorkflowPumpResult.Ran;
        }

        if (verdict.AutoApprove)
        {
            await run.SendResponseAsync(request.CreateResponse(approval.CreateResponse(approved: true, "Approvato dalla policy.")));

            return WorkflowPumpResult.Ran;
        }

        // Nessuna risposta alla richiesta: il run termina con la chiamata ancora sospesa, e il checkpoint la conserva.
        logger.LogInformation(
            "Deal {DealId}: create_order richiede approvazione ({ApprovalReasons})",
            context.DealId, string.Join(", ", verdict.Reasons));

        return new WorkflowPumpResult(verdict.Payload, verdict.Reasons);
    }

    private async Task TrackAgentAsync(
        AgentResponseUpdateEvent update, DealRunContext context, AgentTracker tracker, CancellationToken cancellationToken)
    {
        var agentName = WorkflowAgents.All.FirstOrDefault(a => update.ExecutorId.StartsWith(a.Name, StringComparison.Ordinal))?.Name;

        if (agentName is not null && agentName != tracker.AgentName)
        {
            tracker.Span?.Dispose();
            tracker.Span = OrchestratorTelemetry.Source.StartActivity("agent.run");
            tracker.Span?.SetTag(O2CTelemetry.Attributes.AgentName, agentName);
            tracker.Span?.SetTag(O2CTelemetry.Attributes.CorrelationId, context.CorrelationId);
            tracker.AgentName = agentName;

            await stateStore.UpdateAsync(context.CorrelationId, PhaseOfAgent(agentName), context.ToStateJson(), cancellationToken);
        }

        if (tracker.AgentName is null)
        {
            return;
        }

        foreach (var call in update.Update.Contents.OfType<FunctionCallContent>())
        {
            if (call.Name.StartsWith(HandoffToolPrefix, StringComparison.Ordinal) && tracker.SeenCalls.Add(call.CallId))
            {
                RecordHandoff(context, tracker.AgentName, call);
            }
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
            .Select(tool => Wrap(new GuardedToolFunction(tool, context, scope)))
            .ToList();

        if (definition.StopVerdict is not null)
        {
            agentTools.Add(WorkflowAgents.CreateVerdictTool(definition, context));
        }

        var chatOptions = model.DefaultOptions.Clone();
        chatOptions.Instructions = definition.Instructions;
        chatOptions.Tools = agentTools;

        // Ogni agente vede i fatti prodotti dagli altri (chiamate e risultati) ma non il loro testo (D62).
        var chatClient = new ForeignAgentTextFilter(model.ChatClient, definition.Name, WorkflowAgents.Names);

        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Id = definition.Name,
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = chatOptions
        });
    }

    /// <summary>
    /// Il tool sensibile è dichiarato <see cref="ApprovalRequiredAIFunction"/>: il framework non lo invoca senza una
    /// risposta di approvazione ed espone la chiamata su una porta esterna, che è il punto in cui entra la policy (5.2).
    /// </summary>
    private static AITool Wrap(GuardedToolFunction tool) =>
        tool.QualifiedName == AgentToolNames.CreateOrder ? new ApprovalRequiredAIFunction(tool) : tool;

    /// <summary>Regole di intake verificate dall'host sui fatti del <c>get_deal</c> (§5, D22).</summary>
    internal static bool IntakeRejects(DealDto? deal) =>
        deal is not null
        && (deal.Stage != "ClosedWon"
            || !string.Equals(deal.Currency, "EUR", StringComparison.OrdinalIgnoreCase)
            || deal.LineItems.Count == 0
            || deal.LineItems.Sum(l => l.Quantity * l.UnitPrice) != deal.Amount);

    /// <summary>Esito dai fatti (come in Fase 3) e scrittura sul CRM degli esiti di arresto da parte dell'host (G4.1).</summary>
    public async Task<DealProcessingResult> CompleteAsync(
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

        // Backorder: l'ERP dice cosa manca e in che quantità, e il deal CRM deve riportarlo. Lo scrive l'host, non il
        // modello: è un fatto che non deve dipendere da quanto l'agente è preciso nel ricopiare la nota.
        if (status == DealStatus.OrderCreated && context.Order?.BackorderNote is { Length: > 0 } backorder)
        {
            reasons.Add(backorder);

            await WriteCrmOutcomeAsync(tools, context, DealStatus.OrderCreated, $"Da approvvigionare — {backorder}", cancellationToken);
        }

        return Result(context, status, reasons, model);
    }

    public static DealProcessingResult Result(DealRunContext context, DealStatus status, IReadOnlyList<string> reasons, ModelClient model) =>
        new(context.DealId, context.CorrelationId, status, context.Order?.OrderNumber, reasons, null,
            ModelOutcomeValid: context.IsTerminal, model.Provider, model.ModelId, context.ToolCalls)
        {
            Handoffs = context.Handoffs
        };

    /// <summary>Scrive sul CRM un esito deciso dall'host, non dal modello (arresti, sospensione, rifiuto, scadenza).</summary>
    public async Task WriteCrmOutcomeAsync(
        IReadOnlyList<AgentTool> tools, DealRunContext context, DealStatus status, string note, CancellationToken cancellationToken)
    {
        var scope = new AgentScope(AgentScope.HostName, new HashSet<string> { AgentToolNames.UpdateDeal });
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

    internal static WorkflowPhase PhaseOfAgent(string agentName) =>
        agentName == WorkflowAgents.Intake.Name ? WorkflowPhase.Intake
        : agentName == WorkflowAgents.Fulfillment.Name ? WorkflowPhase.Fulfillment
        : WorkflowPhase.Order;

    internal static WorkflowPhase PhaseOf(DealStatus status) => status switch
    {
        DealStatus.OrderCreated => WorkflowPhase.Completed,
        DealStatus.Discarded => WorkflowPhase.Discarded,
        DealStatus.ApprovalPending => WorkflowPhase.AwaitingApproval,
        _ => WorkflowPhase.Failed
    };
}
