using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Data.Entities;
using Dusiburg.AI.O2C.Crm.Data.Seed;
using Dusiburg.AI.O2C.Crm.Mcp.Messaging;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Crm.Mcp.Dev;

public sealed record DevDealSummary(
    string DealId,
    string Name,
    decimal Amount,
    string Currency,
    string Stage,
    string CompanyId,
    int Revision,
    string? O2CStatus,
    string? ErpOrderNumber,
    DateTimeOffset UpdatedAt);

public sealed record DevDealNote(string Status, string? ErpOrderNumber, string? Note, DateTimeOffset CreatedAt);

public sealed record DevDealDetail(
    string DealId,
    string Name,
    decimal Amount,
    string Currency,
    string Stage,
    string CompanyId,
    string CompanyName,
    int Revision,
    string? O2CStatus,
    string? ErpOrderNumber,
    string? LastNote,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DealLineItemDto> LineItems,
    IReadOnlyList<DevDealNote> Notes);

/// <summary>Endpoint di supporto alla demo (1.9), mappati solo in Development.</summary>
internal static class CrmDevEndpoints
{
    public static IEndpointRouteBuilder MapCrmDevEndpoints(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev");

        dev.MapGet("/deals", ListDealsAsync);
        dev.MapGet("/deals/{dealId}", GetDealAsync);
        dev.MapPost("/deals/{dealId}/close-won", CloseWonAsync);
        dev.MapPost("/reset", ResetAsync);

        return app;
    }

    private static async Task<Ok<List<DevDealSummary>>> ListDealsAsync(CrmDbContext db, CancellationToken cancellationToken)
    {
        var deals = await db.Deals
            .AsNoTracking()
            .Include(d => d.Company)
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(deals
            .Select(d => new DevDealSummary(
                d.Code, d.Name, d.Amount, d.Currency, d.Stage.ToString(), d.Company.Code, d.Revision,
                d.O2CStatus?.ToString(), d.ErpOrderNumber, d.UpdatedAt))
            .ToList());
    }

    private static async Task<Results<Ok<DevDealDetail>, ProblemHttpResult>> GetDealAsync(
        string dealId, CrmDbContext db, CancellationToken cancellationToken)
    {
        var detail = await LoadDetailAsync(db, dealId, cancellationToken);

        return detail is null ? ToolProblems.NotFound($"Deal {dealId} non trovato.") : TypedResults.Ok(detail);
    }

    /// <summary>
    /// Porta il deal in stage <c>ClosedWon</c> (la revisione non cambia: lo stage non è una modifica commerciale, G1.3) e pubblica
    /// <c>deal-closed-won</c> (4.5). Ogni chiamata pubblica: chiamarla due volte produce un evento duplicato, che l'orchestratore ignora.
    /// </summary>
    private static async Task<Results<Ok<DevDealDetail>, ProblemHttpResult>> CloseWonAsync(
        string dealId, CrmDbContext db, TimeProvider timeProvider, DealEventPublisher publisher, CancellationToken cancellationToken)
    {
        var deal = await db.Deals.SingleOrDefaultAsync(d => d.Code == dealId, cancellationToken);

        if (deal is null)
        {
            return ToolProblems.NotFound($"Deal {dealId} non trovato.");
        }

        if (deal.Stage != DealStage.ClosedWon)
        {
            deal.Stage = DealStage.ClosedWon;
            deal.UpdatedAt = timeProvider.GetUtcNow();

            await db.SaveChangesAsync(cancellationToken);
        }

        await publisher.PublishClosedWonAsync(new DealClosedWon(deal.Code, deal.Revision, timeProvider.GetUtcNow()), cancellationToken);

        return TypedResults.Ok((await LoadDetailAsync(db, dealId, cancellationToken))!);
    }

    /// <summary>Riporta aziende, deal e note ai dati demo.</summary>
    private static async Task<NoContent> ResetAsync(CrmDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        await CrmSeeder.ResetAsync(db, timeProvider.GetUtcNow(), cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<DevDealDetail?> LoadDetailAsync(CrmDbContext db, string dealId, CancellationToken cancellationToken)
    {
        var deal = await db.Deals
            .AsNoTracking()
            .Include(d => d.Company)
            .Include(d => d.LineItems)
            .Include(d => d.Notes)
            .AsSplitQuery()
            .SingleOrDefaultAsync(d => d.Code == dealId, cancellationToken);

        return deal is null
            ? null
            : new DevDealDetail(
                deal.Code,
                deal.Name,
                deal.Amount,
                deal.Currency,
                deal.Stage.ToString(),
                deal.Company.Code,
                deal.Company.Name,
                deal.Revision,
                deal.O2CStatus?.ToString(),
                deal.ErpOrderNumber,
                deal.LastNote,
                deal.UpdatedAt,
                deal.LineItems.OrderBy(l => l.Id).Select(l => new DealLineItemDto(l.Sku, l.Quantity, l.UnitPrice)).ToList(),
                deal.Notes.OrderBy(n => n.Id).Select(n => new DevDealNote(n.Status.ToString(), n.ErpOrderNumber, n.Note, n.CreatedAt)).ToList());
    }
}
