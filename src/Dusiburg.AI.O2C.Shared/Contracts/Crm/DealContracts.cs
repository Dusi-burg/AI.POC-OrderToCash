namespace Dusiburg.AI.O2C.Shared.Contracts.Crm;

/// <summary>Input di <c>get_deal</c>.</summary>
public sealed record GetDealRequest(string DealId);

public sealed record DealLineItemDto(string Sku, int Quantity, decimal UnitPrice);

/// <summary>
/// Output di <c>get_deal</c>, con <see cref="Revision"/> in aggiunta a §6.2 (M3): serve alla chiave di idempotenza.
/// </summary>
public sealed record DealDto(
    string DealId,
    int Revision,
    string Name,
    decimal Amount,
    string Currency,
    string Stage,
    string CompanyId,
    IReadOnlyList<DealLineItemDto> LineItems);

/// <summary>Input di <c>update_deal</c>.</summary>
public sealed record UpdateDealRequest(string DealId, string? ErpOrderNumber, DealStatus Status, string? Note);

/// <summary>Output di <c>update_deal</c>.</summary>
public sealed record UpdateDealResponse(bool Ok);
