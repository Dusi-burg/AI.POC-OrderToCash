using Dusiburg.AI.O2C.Erp.Data;
using Dusiburg.AI.O2C.Erp.Data.Seed;

namespace Dusiburg.AI.O2C.Erp.Api.Dev;

/// <summary>Endpoint di supporto alla demo, mappati solo in Development (D32).</summary>
internal static class ErpDevEndpoints
{
    public static IEndpointRouteBuilder MapErpDevEndpoints(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev");

        // Riporta l'ERP ai dati demo: ordini cancellati, giacenze e clienti del seed, numerazione ordini da 1.
        dev.MapPost("/reset", async (ErpDbContext db, CancellationToken cancellationToken) =>
        {
            await ErpSeeder.ResetAsync(db, cancellationToken);

            return TypedResults.NoContent();
        });

        return app;
    }
}
