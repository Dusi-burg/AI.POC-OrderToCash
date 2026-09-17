using System.Diagnostics;
using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Data.Entities;
using Dusiburg.AI.O2C.Crm.Mcp.Messaging;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Dusiburg.AI.O2C.Shared.Correlation;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Crm.Mcp.Deals;

public enum DealCloseResult
{
    /// <summary>Stage aggiornato e, per un deal vinto, evento pubblicato.</summary>
    Closed,

    /// <summary>Deal vinto il cui evento è stato ripubblicato (solo endpoint dev).</summary>
    Republished,

    NotFound,

    /// <summary>Il deal non era più in <see cref="DealStage.ContractSent"/>: nessuna modifica, nessun evento.</summary>
    AlreadyClosed,

    /// <summary>Stage salvato come <see cref="DealStage.ClosedWon"/> ma evento non pubblicato: va ripubblicato.</summary>
    EventNotPublished
}

public sealed record DealCloseOutcomeResult(DealCloseResult Result, string Message);

/// <summary>
/// Unico punto di chiusura dei deal (6.2, D59, G6.3): la transizione parte solo da <see cref="DealStage.ContractSent"/>
/// ed è un UPDATE condizionale, così due chiusure simultanee non pubblicano due eventi (stessa idea di D56).
/// La revisione non cambia: lo stage non è una modifica commerciale (G1.3).
/// </summary>
public sealed class DealClosingService(
    CrmDbContext db,
    IDealEventPublisher publisher,
    ICorrelationContext correlationContext,
    TimeProvider timeProvider,
    ILogger<DealClosingService> logger)
{
    public const string CloseActivityName = "crm.deal.close";

    private static readonly ActivitySource Source = new(O2CTelemetry.Sources.McpCrm);

    public async Task<DealCloseOutcomeResult> CloseAsync(string dealId, DealCloseOutcome outcome, CancellationToken cancellationToken)
    {
        using Activity? activity = StartCloseActivity(dealId, outcome);

        Deal? deal = await db.Deals.AsNoTracking().SingleOrDefaultAsync(d => d.Code == dealId, cancellationToken);

        if (deal is null)
        {
            return new(DealCloseResult.NotFound, $"Deal {dealId} non trovato.");
        }

        DealStage target = outcome == DealCloseOutcome.Won ? DealStage.ClosedWon : DealStage.ClosedLost;
        DateTimeOffset now = timeProvider.GetUtcNow();

        int updated = await db.Deals
            .Where(d => d.Id == deal.Id && d.Stage == DealStage.ContractSent)
            .ExecuteUpdateAsync(setters => setters.SetProperty(d => d.Stage, target).SetProperty(d => d.UpdatedAt, now), cancellationToken);

        if (updated == 0)
        {
            activity?.SetTag(O2CTelemetry.Attributes.ToolOutcome, nameof(DealCloseResult.AlreadyClosed));

            return new(DealCloseResult.AlreadyClosed, $"Il deal {dealId} è già chiuso: nessuna modifica.");
        }

        logger.LogInformation("Deal {DealId} chiuso come {Stage}", dealId, target);

        if (outcome == DealCloseOutcome.Lost)
        {
            activity?.SetTag(O2CTelemetry.Attributes.ToolOutcome, nameof(DealCloseResult.Closed));

            return new(DealCloseResult.Closed, $"Deal {dealId} chiuso come perso: nessun ordine verrà generato.");
        }

        return await PublishAsync(deal, DealCloseResult.Closed, activity, cancellationToken);
    }

    /// <summary>
    /// Per l'endpoint dev <c>close-won</c> (G6.4): chiude come vinto un deal aperto, oppure ripubblica l'evento di un deal
    /// già vinto (l'orchestratore riconosce il duplicato). Un deal perso resta perso.
    /// </summary>
    public async Task<DealCloseOutcomeResult> CloseWonOrRepublishAsync(string dealId, CancellationToken cancellationToken)
    {
        Deal? deal = await db.Deals.AsNoTracking().SingleOrDefaultAsync(d => d.Code == dealId, cancellationToken);

        if (deal?.Stage != DealStage.ClosedWon)
        {
            return await CloseAsync(dealId, DealCloseOutcome.Won, cancellationToken);
        }

        using Activity? activity = StartCloseActivity(dealId, DealCloseOutcome.Won);

        return await PublishAsync(deal, DealCloseResult.Republished, activity, cancellationToken);
    }

    private async Task<DealCloseOutcomeResult> PublishAsync(
        Deal deal, DealCloseResult successResult, Activity? activity, CancellationToken cancellationToken)
    {
        try
        {
            string correlationId = await publisher.PublishClosedWonAsync(
                new DealClosedWon(deal.Code, deal.Revision, timeProvider.GetUtcNow()), cancellationToken);

            activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, correlationId);
            activity?.SetTag(O2CTelemetry.Attributes.ToolOutcome, successResult.ToString());

            return new(successResult, $"Deal {deal.Code} chiuso come vinto: il flusso Order-to-Cash è partito.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Lo stage resta ClosedWon: senza outbox (G6.3) il rimedio è ripubblicare dall'endpoint dev.
            logger.LogError(exception, "Deal {DealId} chiuso come vinto ma evento deal-closed-won non pubblicato", deal.Code);

            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag(O2CTelemetry.Attributes.ToolOutcome, nameof(DealCloseResult.EventNotPublished));

            return new(DealCloseResult.EventNotPublished,
                $"Il deal {deal.Code} è chiuso come vinto, ma l'evento non è stato pubblicato: il flusso non è partito.");
        }
    }

    private Activity? StartCloseActivity(string dealId, DealCloseOutcome outcome)
    {
        Activity? activity = Source.StartActivity(CloseActivityName);

        activity?.SetTag(O2CTelemetry.Attributes.DealId, dealId);
        activity?.SetTag(O2CTelemetry.Attributes.DealCloseOutcome, outcome.ToString());
        activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, correlationContext.Current);

        return activity;
    }
}
