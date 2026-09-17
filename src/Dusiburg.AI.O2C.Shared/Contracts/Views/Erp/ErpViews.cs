using Dusiburg.AI.O2C.Shared.Contracts.Erp;

namespace Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;

// Viste dell'ERP per Erp.Web (Fase 6, G6.1-G6.2): esposte da GET /api/views su Erp.Api, in sola lettura e separate dai contratti dei tool di §6.1.

public sealed record CustomerSummaryView(
    int CustomerId,
    string Name,
    string? VatNumber,
    string Email,
    decimal CreditLimit,
    bool IsBlocked,
    int OrderCount);

public sealed record CustomerDetailView(
    int CustomerId,
    string Name,
    string? VatNumber,
    string Email,
    string Address,
    decimal CreditLimit,
    bool IsBlocked,
    IReadOnlyList<OrderSummaryView> Orders);

/// <summary>
/// Giacenza di un prodotto. <see cref="Available"/> = <c>OnHand − Reserved</c> e può essere negativo: la riserva
/// di un ordine in backorder è incondizionata (D55).
/// </summary>
public sealed record StockItemView(
    string Sku,
    string Description,
    string Uom,
    decimal ListPrice,
    int OnHand,
    int Reserved,
    int Available,
    int LeadTimeDays);

public sealed record OrderSummaryView(
    Guid OrderId,
    string OrderNumber,
    int CustomerId,
    string CustomerName,
    decimal Total,
    OrderStatus Status,
    string ExternalRef,
    bool HasBackorder,
    DateTimeOffset CreatedAt);

public sealed record OrderLineView(string Sku, string Description, string Uom, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

public sealed record OrderDetailView(
    OrderSummaryView Order,
    string IdempotencyKey,
    string? BackorderNote,
    IReadOnlyList<OrderLineView> Lines);
