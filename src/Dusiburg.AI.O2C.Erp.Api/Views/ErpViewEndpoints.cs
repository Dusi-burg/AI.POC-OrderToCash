using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.O2C.Erp.Api.Views;

/// <summary>
/// Letture dell'ERP per <c>Erp.Web</c> (6.3): solo GET, su <c>/api/views</c>, separate dagli endpoint dei tool di §6.1
/// (<c>GET /api/customers</c> resta la ricerca di <c>get_customer</c>).
/// </summary>
internal static class ErpViewEndpoints
{
    public static RouteGroupBuilder MapErpViewEndpoints(this RouteGroupBuilder api)
    {
        var views = api.MapGroup("/views");

        views.MapGet("/customers", ListCustomersAsync);
        views.MapGet("/customers/{customerId:int}", GetCustomerAsync);
        views.MapGet("/stock", ListStockAsync);
        views.MapGet("/orders", ListOrdersAsync);
        views.MapGet("/orders/{orderNumber}", GetOrderAsync);

        return api;
    }

    private static async Task<Ok<IReadOnlyList<CustomerSummaryView>>> ListCustomersAsync(
        ErpViewQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.ListCustomersAsync(cancellationToken));

    private static async Task<Results<Ok<CustomerDetailView>, ProblemHttpResult>> GetCustomerAsync(
        int customerId, ErpViewQueries queries, CancellationToken cancellationToken)
    {
        CustomerDetailView? customer = await queries.FindCustomerAsync(customerId, cancellationToken);

        return customer is null ? ToolProblems.NotFound($"Cliente {customerId} non trovato.") : TypedResults.Ok(customer);
    }

    private static async Task<Ok<IReadOnlyList<StockItemView>>> ListStockAsync(
        bool? shortOnly, ErpViewQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.ListStockAsync(shortOnly ?? false, cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<OrderSummaryView>>, ProblemHttpResult>> ListOrdersAsync(
        string? status, int? customerId, ErpViewQueries queries, CancellationToken cancellationToken)
    {
        OrderStatus? statusFilter = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out OrderStatus parsed) || !Enum.IsDefined(parsed) || int.TryParse(status, out _))
            {
                return ToolProblems.Validation($"Filtro status non valido: ammessi {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
            }

            statusFilter = parsed;
        }

        return TypedResults.Ok(await queries.ListOrdersAsync(new OrderListFilter(statusFilter, customerId), cancellationToken));
    }

    private static async Task<Results<Ok<OrderDetailView>, ProblemHttpResult>> GetOrderAsync(
        string orderNumber, ErpViewQueries queries, CancellationToken cancellationToken)
    {
        OrderDetailView? order = await queries.FindOrderAsync(orderNumber, cancellationToken);

        return order is null ? ToolProblems.NotFound($"Ordine {orderNumber} non trovato.") : TypedResults.Ok(order);
    }
}
