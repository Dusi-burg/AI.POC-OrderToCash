using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;

namespace Dusiburg.AI.O2C.Erp.Web.Api;

/// <summary>Regole di presentazione dell'ERP: evidenza del magazzino e colori degli stati d'ordine.</summary>
public static class ErpPresentation
{
    /// <summary>Niente da vendere: il disponibile è zero o negativo.</summary>
    public static bool IsShort(StockItemView item) => item.Available <= 0;

    /// <summary>Riservato oltre la giacenza: pezzi promessi da ordini in backorder e ancora da approvvigionare (D55).</summary>
    public static bool IsOverReserved(StockItemView item) => item.Reserved > item.OnHand;

    public static string StockRowClass(StockItemView item) =>
        IsOverReserved(item) ? "table-danger" : IsShort(item) ? "table-warning" : string.Empty;

    public static string OrderStatusClass(OrderStatus status) => status switch
    {
        OrderStatus.Confirmed => "text-bg-success",
        OrderStatus.Backorder => "text-bg-warning",
        _ => "text-bg-secondary"
    };
}

/// <summary>Riepilogo della home: clienti, ordini per stato, prodotti sotto scorta, ultimi ordini.</summary>
public sealed record ErpDashboard(
    int CustomerCount,
    int BlockedCustomerCount,
    IReadOnlyDictionary<OrderStatus, int> OrdersByStatus,
    IReadOnlyList<StockItemView> ShortStock,
    IReadOnlyList<OrderSummaryView> LatestOrders);

public sealed class ErpDashboardService(ErpApiClient erp)
{
    public const int LatestOrderCount = 5;

    public async Task<ErpDashboard> LoadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomerSummaryView> customers = await erp.ListCustomersAsync(cancellationToken);
        IReadOnlyList<OrderSummaryView> orders = await erp.ListOrdersAsync(null, null, cancellationToken);
        IReadOnlyList<StockItemView> shortStock = await erp.ListStockAsync(shortOnly: true, cancellationToken);

        Dictionary<OrderStatus, int> ordersByStatus = Enum.GetValues<OrderStatus>()
            .ToDictionary(status => status, status => orders.Count(o => o.Status == status));

        return new ErpDashboard(
            customers.Count,
            customers.Count(c => c.IsBlocked),
            ordersByStatus,
            shortStock,
            [.. orders.Take(LatestOrderCount)]);
    }
}
