using Dusiburg.AI.O2C.Orchestrator.Telemetry;
using Dusiburg.AI.O2C.Orchestrator.Workflow;
using Dusiburg.AI.O2C.Shared.Telemetry;

namespace Dusiburg.AI.O2C.Orchestrator.Governance;

/// <summary>
/// Le due sweep periodiche sulle approvazioni, eseguite all'avvio e poi a intervallo regolare:
/// <list type="bullet">
/// <item><b>Riconciliazione</b> (5.5, G5.3): richieste già decise il cui workflow è ancora sospeso. È la rete di sicurezza
/// che rende il messaggio <c>approval-decided</c> un acceleratore: se si perde, il workflow riparte comunque.</item>
/// <item><b>Scadenza</b> (5.6): richieste pendenti oltre <c>APPROVAL_TIMEOUT_HOURS</c>, portate a <c>Expired</c> con la
/// concorrenza ottimistica di <c>RowVersion</c>, così una decisione che arriva nello stesso istante vince o perde una
/// volta sola. Nessun ordine viene creato; il deal viene segnato <c>Expired</c> con una nota (G5.6).</item>
/// </list>
/// </summary>
public sealed class ApprovalSweepService(
    IApprovalStore approvals,
    ApprovalResumeRunner resumer,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ApprovalSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = ApprovalSweepOptions.FromConfiguration(configuration);

        logger.LogInformation(
            "Sweep delle approvazioni ogni {SweepInterval}, scadenza dopo {ApprovalTimeout}", options.Interval, options.Timeout);

        using var timer = new PeriodicTimer(options.Interval, timeProvider);

        do
        {
            try
            {
                await ExpireAsync(options, stoppingToken);
                await ReconcileAsync(options, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Una sweep fallita non deve fermare il servizio: al prossimo giro ritrova le stesse righe.
                logger.LogError(exception, "Sweep delle approvazioni fallita");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    /// <summary>Scadenze: la transizione a <c>Expired</c> e la scrittura sul CRM passano dalle stesse strade di una decisione.</summary>
    internal async Task ExpireAsync(ApprovalSweepOptions options, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - options.Timeout;

        foreach (var approval in await approvals.ListExpiredAsync(cutoff, cancellationToken))
        {
            if (!await approvals.TryExpireAsync(approval.ApprovalId, cancellationToken))
            {
                logger.LogInformation("Richiesta {ApprovalId} decisa mentre scadeva: nessuna scadenza", approval.ApprovalId);

                continue;
            }

            using var activity = OrchestratorTelemetry.Source.StartActivity(OrchestratorTelemetry.ApprovalExpiredActivityName);
            activity?.SetTag(O2CTelemetry.Attributes.ApprovalId, approval.ApprovalId);
            activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, approval.CorrelationId);
            activity?.SetTag(O2CTelemetry.Attributes.DealId, approval.DealId);

            logger.LogWarning(
                "Richiesta {ApprovalId} sul deal {DealId} scaduta dopo {ApprovalTimeout}: nessun ordine",
                approval.ApprovalId, approval.DealId, options.Timeout);

            await resumer.ResumeAsync(approval.ApprovalId, cancellationToken);
        }
    }

    /// <summary>
    /// Riconciliazione: chiude i workflow rimasti sospesi con una decisione già presa, e recupera quelli il cui
    /// possesso di ripresa è rimasto appeso oltre <see cref="ApprovalSweepOptions.ResumeClaimTimeout"/>.
    /// </summary>
    internal async Task ReconcileAsync(ApprovalSweepOptions options, CancellationToken cancellationToken)
    {
        var staleResumeBefore = timeProvider.GetUtcNow() - options.ResumeClaimTimeout;

        foreach (var approvalId in await approvals.ListAwaitingResumeAsync(staleResumeBefore, cancellationToken))
        {
            var outcome = await resumer.ResumeAsync(approvalId, cancellationToken);

            if (outcome is ApprovalResumeOutcome.Completed or ApprovalResumeOutcome.Closed)
            {
                logger.LogInformation("Riconciliazione: workflow di {ApprovalId} ripreso ({ResumeOutcome})", approvalId, outcome);
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
