using Dusiburg.AI.O2C.Erp.Data;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Erp.Api.Stock;

internal static class StockEndpoints
{
    public static RouteGroupBuilder MapStockEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stock/{sku}", CheckStockAsync);

        return api;
    }

    /// <summary><c>check_stock</c>: disponibile se <c>OnHand − Reserved ≥ quantity</c> (D21).</summary>
    private static async Task<Results<Ok<StockCheckDto>, ProblemHttpResult>> CheckStockAsync(
        string sku, int? quantity, ErpDbContext db, CancellationToken cancellationToken)
    {
        if (quantity is null or <= 0)
        {
            return ToolProblems.Validation("quantity è obbligatoria e deve essere maggiore di zero.");
        }

        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.StockLevel)
            .SingleOrDefaultAsync(p => p.Sku == sku, cancellationToken);

        if (product is null)
        {
            return ToolProblems.NotFound($"SKU {sku} non trovato.");
        }

        var onHand = product.StockLevel?.OnHand ?? 0;
        var reserved = product.StockLevel?.Reserved ?? 0;

        return TypedResults.Ok(new StockCheckDto(product.Sku, onHand - reserved >= quantity, onHand, product.StockLevel?.LeadTimeDays ?? 0));
    }
}
