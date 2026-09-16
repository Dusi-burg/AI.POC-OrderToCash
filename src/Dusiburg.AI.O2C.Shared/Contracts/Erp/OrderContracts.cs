namespace Dusiburg.AI.O2C.Shared.Contracts.Erp;

/// <summary>Riga di <c>create_order</c>: vale il prezzo unitario del deal (D21).</summary>
public sealed record OrderLineInput(string Sku, int Quantity, decimal UnitPrice);

/// <summary>
/// Input di <c>create_order</c>. <see cref="IdempotencyKey"/> è calcolata dal codice
/// (<c>IdempotencyKey.From</c>), mai dal modello (D17); <see cref="ExternalRef"/> è il dealId.
/// </summary>
public sealed record CreateOrderRequest(
    int CustomerId,
    IReadOnlyList<OrderLineInput> Lines,
    string ExternalRef,
    string IdempotencyKey);

/// <summary>
/// Output di <c>create_order</c>. <see cref="BackorderNote"/> è valorizzato solo per un ordine in backorder e dice, in
/// chiaro, cosa manca e in che quantità (es. <c>IND-MOT-003: 2 PZ da ordinare</c>): l'ordine è accettato e lo stock
/// riservato, ma quei pezzi vanno approvvigionati. L'orchestratore lo riporta sulla nota del deal CRM.
/// </summary>
public sealed record CreateOrderResponse(Guid OrderId, string OrderNumber, decimal Total, OrderStatus Status, string? BackorderNote = null);

/// <summary>Input di <c>get_order</c>.</summary>
public sealed record GetOrderRequest(Guid OrderId);

public sealed record OrderLineDto(int OrderLineId, string Sku, int Quantity, decimal UnitPrice);

/// <summary>Output di <c>get_order</c>: ordine completo con righe.</summary>
public sealed record OrderDto(
    Guid OrderId,
    string OrderNumber,
    int CustomerId,
    decimal Total,
    OrderStatus Status,
    string ExternalRef,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OrderLineDto> Lines,
    string? BackorderNote = null);
