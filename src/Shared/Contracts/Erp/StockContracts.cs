namespace O2C.Shared.Contracts.Erp;

/// <summary>Input di <c>check_stock</c>.</summary>
public sealed record CheckStockRequest(string Sku, int Quantity);

/// <summary>
/// Output di <c>check_stock</c>: <see cref="Available"/> vale <c>OnHand − Reserved ≥ quantity</c> (D21).
/// </summary>
public sealed record StockCheckDto(string Sku, bool Available, int OnHand, int LeadTimeDays);
