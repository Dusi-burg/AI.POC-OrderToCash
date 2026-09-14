using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.O2C.Erp.Api.Orders;

internal static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/orders", CreateOrderAsync);
        api.MapGet("/orders/{orderId:guid}", GetOrderAsync);

        return api;
    }

    /// <summary>201 ordine creato · 200 ordine già esistente con la stessa chiave · 400 · 404 cliente o SKU sconosciuti.</summary>
    private static async Task<Results<Created<CreateOrderResponse>, Ok<CreateOrderResponse>, ProblemHttpResult>> CreateOrderAsync(
        CreateOrderRequest request, OrderService orders, CancellationToken cancellationToken)
    {
        var result = await orders.CreateAsync(request, cancellationToken);

        return result.Outcome switch
        {
            CreateOrderOutcome.Created => TypedResults.Created($"/api/orders/{result.Order!.OrderId}", result.Order),
            CreateOrderOutcome.Existing => TypedResults.Ok(result.Order!),
            CreateOrderOutcome.NotFound => ToolProblems.NotFound(result.Error!),
            _ => ToolProblems.Validation(result.Error!)
        };
    }

    private static async Task<Results<Ok<OrderDto>, ProblemHttpResult>> GetOrderAsync(
        Guid orderId, OrderService orders, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken);

        return order is null
            ? ToolProblems.NotFound($"Ordine {orderId} non trovato.")
            : TypedResults.Ok(order);
    }
}
