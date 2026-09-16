using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Governance;
using Dusiburg.AI.O2C.Orchestrator.Telemetry;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Workflow;

/// <summary>
/// Workflow a tre agenti con handoff di Agent Framework (4.3, D44). Gli agenti chiamano i tool e i trasferimenti;
/// l'host legge il deal, persiste lo stato, termina sui fatti, decide l'esito e scrive sul CRM gli esiti di arresto (G4.1).
/// <para>
/// Dalla Fase 5 il run è checkpointato (sessione = correlation id): se la policy chiede un'approvazione il workflow si
/// ferma con <c>create_order</c> ancora da eseguire e la ripresa avviene altrove, anche in un altro processo (D16).
/// </para>
/// </summary>
public sealed class DealWorkflowRunner(
    DealWorkflowEngine engine,
    IWorkflowStateStore stateStore,
    CheckpointManager checkpoints,
    IApprovalStore approvalStore,
    ILogger<DealWorkflowRunner> logger) : IDealAgent
{
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
        var tools = await engine.GetToolsAsync(cancellationToken);
        var probe = new DealRunContext(dealId, correlationId, "Host", new HashSet<string>());
        var revision = dealRevision ?? await ReadRevisionAsync(tools, probe, cancellationToken);

        if (!await stateStore.TryStartAsync(correlationId, dealId, revision, reprocess, cancellationToken))
        {
            logger.LogInformation("Deal {DealId} alla revisione {DealRevision} già ricevuto: nessun nuovo workflow", dealId, revision);

            return null;
        }

        var context = new DealRunContext(dealId, correlationId, "Host", new HashSet<string>());

        using var setup = await engine.BuildAsync(context, cancellationToken);

        WorkflowPumpResult pumped;
        string? checkpoint;

        // Il run si chiude prima di qualsiasi effetto collaterale: le scritture su CRM e database avvengono a workflow fermo.
        await using (var run = await InProcessExecution.RunStreamingAsync(
            setup.Workflow,
            new List<ChatMessage> { new(ChatRole.User, $"Process CRM deal {dealId}.") },
            checkpoints,
            correlationId,
            cancellationToken))
        {
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            pumped = await engine.PumpAsync(run, context, setup.Tools, cancellationToken);

            // Il checkpoint dell'ultimo superstep contiene la chiamata a create_order in attesa di risposta.
            checkpoint = run.LastCheckpoint?.CheckpointId;
        }

        if (pumped.Suspended is { } proposal)
        {
            return await SuspendAsync(setup, context, revision, proposal, pumped.Reasons, checkpoint, cancellationToken);
        }

        var result = await engine.CompleteAsync(context, setup.Tools, setup.Model, cancellationToken);

        await stateStore.UpdateAsync(correlationId, DealWorkflowEngine.PhaseOf(result.Status), context.ToStateJson(), cancellationToken);

        return result;
    }

    /// <summary>
    /// Sospensione (5.2): nessuna chiamata all'ERP, richiesta di approvazione e stato persistiti insieme al checkpoint da
    /// cui riprendere, deal segnato <c>ApprovalPending</c> con i motivi. Dopo questo metodo non resta nulla in memoria.
    /// </summary>
    private async Task<DealProcessingResult> SuspendAsync(
        WorkflowRunSetup setup,
        DealRunContext context,
        int revision,
        ApprovalPayload proposal,
        IReadOnlyList<ApprovalReason> reasons,
        string? checkpoint,
        CancellationToken cancellationToken)
    {
        using var activity = OrchestratorTelemetry.Source.StartActivity(OrchestratorTelemetry.ApprovalRequestedActivityName);
        activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, context.CorrelationId);
        activity?.SetTag(O2CTelemetry.Attributes.DealId, context.DealId);
        activity?.SetTag(O2CTelemetry.Attributes.ApprovalReasons, string.Join(",", reasons));

        if (checkpoint is null)
        {
            logger.LogError("Deal {DealId}: nessun checkpoint disponibile alla sospensione", context.DealId);
        }

        var approvalId = await approvalStore.CreatePendingAsync(
            context, revision, proposal, reasons, checkpoint, activity?.Id ?? Activity.Current?.Id, cancellationToken);

        activity?.SetTag(O2CTelemetry.Attributes.ApprovalId, approvalId);

        var note = $"Approvazione richiesta: {string.Join(", ", reasons)}. Totale {proposal.Total:N2} EUR.";

        await engine.WriteCrmOutcomeAsync(setup.Tools, context, DealStatus.ApprovalPending, note, cancellationToken);

        await stateStore.UpdateAsync(
            context.CorrelationId, WorkflowPhase.AwaitingApproval, context.ToStateJson(), cancellationToken);

        logger.LogInformation(
            "Deal {DealId} sospeso in attesa di approvazione {ApprovalId} (checkpoint {CheckpointId})",
            context.DealId, approvalId, checkpoint);

        return DealWorkflowEngine.Result(context, DealStatus.ApprovalPending, [note], setup.Model);
    }

    /// <summary>Lettura deterministica del deal da parte dell'host: serve la revisione per l'idempotenza prima di avviare gli agenti.</summary>
    private static async Task<int> ReadRevisionAsync(IReadOnlyList<AgentTool> tools, DealRunContext context, CancellationToken cancellationToken)
    {
        var getDeal = tools.Single(t => t.QualifiedName == AgentToolNames.GetDeal).Function;
        var result = ToolResultReader.Read(await getDeal.InvokeAsync(new AIFunctionArguments { ["dealId"] = context.DealId }, cancellationToken));

        return result.Succeeded && result.Data.Deserialize<DealDto>(AgentJson.Options) is { } deal ? deal.Revision : 0;
    }
}
