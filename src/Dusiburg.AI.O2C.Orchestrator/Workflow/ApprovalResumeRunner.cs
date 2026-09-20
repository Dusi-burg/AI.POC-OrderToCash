using System.Diagnostics;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Governance;
using Dusiburg.AI.O2C.Orchestrator.Telemetry;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Correlation;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Workflow;

/// <summary>Esito del tentativo di riprendere un workflow sospeso.</summary>
public enum ApprovalResumeOutcome
{
    /// <summary>La richiesta non esiste.</summary>
    NotFound,

    /// <summary>La richiesta è ancora pendente: non c'è nulla da riprendere.</summary>
    StillPending,

    /// <summary>Il workflow non è (più) in attesa: già ripreso da un'altra consegna o dalla sweep. Nessuna azione.</summary>
    AlreadyResumed,

    /// <summary>L'ordine è stato creato e il deal aggiornato.</summary>
    Completed,

    /// <summary>Nessun ordine: il deal è stato segnato con l'esito della decisione.</summary>
    Closed
}

/// <summary>
/// Riprende un workflow sospeso quando la decisione è arrivata (5.5). È idempotente: la prima ripresa porta il workflow
/// fuori da <see cref="WorkflowPhase.AwaitingApproval"/>, e ogni consegna successiva — messaggio duplicato o sweep di
/// riconciliazione — non fa nulla. Lo span di ripresa ha come parent il contesto salvato alla sospensione (G5.4).
/// </summary>
public sealed class ApprovalResumeRunner(
    DealWorkflowEngine engine,
    IApprovalStore approvals,
    CheckpointManager checkpoints,
    ICorrelationContext correlationContext,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ApprovalResumeRunner> logger)
{
    public async Task<ApprovalResumeOutcome> ResumeAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        if (await approvals.FindAsync(approvalId, cancellationToken) is not { } approval)
        {
            logger.LogWarning("Richiesta di approvazione {ApprovalId} non trovata", approvalId);

            return ApprovalResumeOutcome.NotFound;
        }

        if (approval.Status == ApprovalStatus.Pending)
        {
            return ApprovalResumeOutcome.StillPending;
        }

        // Possesso atomico: chi non vince la corsa non fa nulla. Una lettura della fase non basterebbe, perché il
        // messaggio della decisione e la sweep di riconciliazione possono leggerla insieme e trovarla entrambi in attesa.
        var staleResumeBefore = timeProvider.GetUtcNow() - ApprovalSweepOptions.FromConfiguration(configuration).ResumeClaimTimeout;

        if (!await approvals.TryClaimResumeAsync(approval.CorrelationId, staleResumeBefore, cancellationToken))
        {
            return ApprovalResumeOutcome.AlreadyResumed;
        }

        using var correlationScope = correlationContext.Begin(approval.CorrelationId);
        using var logScope = logger.BeginScope(new Dictionary<string, object> { [O2CTelemetry.Attributes.CorrelationId] = approval.CorrelationId });

        ActivityContext.TryParse(approval.TraceParent, null, out var parentContext);

        using var activity = OrchestratorTelemetry.Source.StartActivity(
            OrchestratorTelemetry.ApprovalResumeActivityName, ActivityKind.Internal, parentContext);

        activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, approval.CorrelationId);
        activity?.SetTag(O2CTelemetry.Attributes.DealId, approval.DealId);
        activity?.SetTag(O2CTelemetry.Attributes.ApprovalId, approvalId);
        activity?.SetTag(O2CTelemetry.Attributes.ApprovalDecision, approval.Status.ToString());
        activity?.SetTag(O2CTelemetry.Attributes.ApprovalDecidedBy, approval.DecidedBy);
        activity?.SetBaggage(O2CTelemetry.Attributes.CorrelationId, approval.CorrelationId);

        var snapshot = await approvals.GetSnapshotAsync(approval.CorrelationId, cancellationToken)
            ?? new DealRunSnapshot(approval.DealId, approval.CorrelationId, null, null, [], null, false, null, null, null, [], []);

        var context = DealRunContext.Restore(snapshot, AgentScope.HostName, new HashSet<string>());

        return approval.Status == ApprovalStatus.Approved
            ? await ApproveAsync(approval, context, cancellationToken)
            : await CloseWithoutOrderAsync(approval, context, cancellationToken);
    }

    /// <summary>
    /// Approvazione: si riprende il run dal checkpoint e si risponde alla richiesta che lo aveva fermato. <c>create_order</c>
    /// parte con la stessa chiave di idempotenza della proposta, quindi anche una doppia ripresa lascia un solo ordine.
    /// </summary>
    private async Task<ApprovalResumeOutcome> ApproveAsync(PendingApproval approval, DealRunContext context, CancellationToken cancellationToken)
    {
        if (approval.CheckpointId is not { } checkpointId)
        {
            logger.LogError("Approvazione {ApprovalId} senza checkpoint: il workflow non può riprendere", approval.ApprovalId);

            return await CloseWithoutOrderAsync(approval, context, cancellationToken);
        }

        using var setup = await engine.BuildAsync(context, cancellationToken);

        var answer = new ApprovalAnswer(true, approval.DecisionNote ?? $"Approvato da {approval.DecidedBy}.");

        // Come all'avvio, il run si chiude prima degli effetti collaterali dell'host.
        await using (var run = await InProcessExecution.ResumeStreamingAsync(
            setup.Workflow, new CheckpointInfo(approval.CorrelationId, checkpointId), checkpoints, cancellationToken))
        {
            // Il run ripreso riemette la richiesta pendente; la risposta apre un altro batch, che il pump segue da sé.
            await engine.PumpAsync(run, context, setup.Tools, cancellationToken, answer);
        }

        var result = await engine.CompleteAsync(context, setup.Tools, setup.Model, cancellationToken);

        await approvals.CloseWorkflowAsync(
            approval.CorrelationId, DealWorkflowEngine.PhaseOf(result.Status), context.ToStateJson(), cancellationToken);

        logger.LogInformation(
            "Workflow di {DealId} ripreso dopo l'approvazione {ApprovalId}: {DealOutcome} {ErpOrderNumber}",
            approval.DealId, approval.ApprovalId, result.Status, result.ErpOrderNumber);

        return result.Status == DealStatus.OrderCreated ? ApprovalResumeOutcome.Completed : ApprovalResumeOutcome.Closed;
    }

    /// <summary>
    /// Rifiuto e scadenza non riaprono il workflow: non c'è nessun ordine da creare e far ripartire il modello per
    /// comunicargli un rifiuto aggiungerebbe solo incertezza. L'host scrive l'esito sul CRM e chiude il workflow.
    /// </summary>
    private async Task<ApprovalResumeOutcome> CloseWithoutOrderAsync(
        PendingApproval approval, DealRunContext context, CancellationToken cancellationToken)
    {
        var status = approval.Status == ApprovalStatus.Expired ? DealStatus.Expired : DealStatus.Rejected;
        var tools = await engine.GetToolsAsync(cancellationToken);

        var note = status == DealStatus.Expired
            ? $"Richiesta di approvazione scaduta senza decisione ({string.Join(", ", approval.Reasons)})."
            : $"Approvazione rifiutata da {approval.DecidedBy ?? "(sconosciuto)"}: {approval.DecisionNote ?? "nessuna nota"}.";

        await engine.WriteCrmOutcomeAsync(tools, context, status, note, cancellationToken);

        await approvals.CloseWorkflowAsync(approval.CorrelationId, WorkflowPhase.Completed, context.ToStateJson(), cancellationToken);

        return ApprovalResumeOutcome.Closed;
    }
}
