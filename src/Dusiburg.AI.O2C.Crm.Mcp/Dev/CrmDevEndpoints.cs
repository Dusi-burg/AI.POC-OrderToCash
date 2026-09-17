using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Data.Seed;
using Dusiburg.AI.O2C.Crm.Mcp.Deals;
using Dusiburg.AI.O2C.Crm.Mcp.Views;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.O2C.Crm.Mcp.Dev;

/// <summary>
/// Endpoint di supporto alla demo (1.9), mappati solo in Development. Le letture dei deal sono passate alle viste di
/// <c>/api/views</c> (G6.4); restano la chiusura per gli script, con la ripubblicazione, e il reset.
/// </summary>
internal static class CrmDevEndpoints
{
    public static IEndpointRouteBuilder MapCrmDevEndpoints(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev");

        dev.MapPost("/deals/{dealId}/close-won", CloseWonAsync);
        dev.MapPost("/reset", ResetAsync);

        return app;
    }

    /// <summary>
    /// Chiude come vinto un deal aperto, oppure ripubblica <c>deal-closed-won</c> per un deal già vinto: chiamarla due volte
    /// produce un evento duplicato, che l'orchestratore ignora. Su un deal perso risponde 409.
    /// </summary>
    private static async Task<Results<Ok<DealDetailView>, ProblemHttpResult>> CloseWonAsync(
        string dealId, DealClosingService closing, CrmViewQueries queries, CancellationToken cancellationToken)
    {
        DealCloseOutcomeResult result = await closing.CloseWonOrRepublishAsync(dealId, cancellationToken);

        return await CrmApiEndpoints.ToHttpResultAsync(result, dealId, queries, cancellationToken);
    }

    /// <summary>Riporta aziende, deal e note ai dati demo.</summary>
    private static async Task<NoContent> ResetAsync(CrmDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        await CrmSeeder.ResetAsync(db, timeProvider.GetUtcNow(), cancellationToken);

        return TypedResults.NoContent();
    }
}
