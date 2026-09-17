using Dusiburg.AI.O2C.Erp.Data;
using Dusiburg.AI.O2C.Erp.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Erp.Api.Views;

/// <summary>Filtri dell'elenco degli ordini (G6.6); un valore <c>null</c> non filtra.</summary>
public sealed record OrderListFilter(OrderStatus? Status, int? CustomerId);

/// <summary>
/// Letture dell'ERP per <c>Erp.Web</c> (6.3), in sola lettura: query senza tracking, nessuna paginazione ma un limite
/// fisso di righe con ordinamento stabile (G6.6).
/// </summary>
public sealed class ErpViewQueries(ErpDbContext db)
{
    public const int MaxRows = 200;

    public async Task<IReadOnlyList<CustomerSummaryView>> ListCustomersAsync(CancellationToken cancellationToken)
    {
        List<CustomerSummaryView> customers = await db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Take(MaxRows)
            .Select(c => new CustomerSummaryView(c.Id, c.Name, c.VatNumber, c.Email, c.CreditLimit, c.IsBlocked, c.Orders.Count))
            .ToListAsync(cancellationToken);

        return customers;
    }

    public async Task<CustomerDetailView?> FindCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        Customer? customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        if (customer is null)
        {
            return null;
        }

        IReadOnlyList<OrderSummaryView> orders = await ListOrdersAsync(new OrderListFilter(null, customerId), cancellationToken);

        return new CustomerDetailView(
            customer.Id, customer.Name, customer.VatNumber, customer.Email, customer.Address, customer.CreditLimit, customer.IsBlocked, orders);
    }

    /// <summary>Magazzino per SKU; con <paramref name="shortOnly"/> solo i prodotti con disponibile ≤ 0.</summary>
    public async Task<IReadOnlyList<StockItemView>> ListStockAsync(bool shortOnly, CancellationToken cancellationToken)
    {
        IQueryable<Product> products = db.Products.AsNoTracking();

        if (shortOnly)
        {
            products = products.Where(p => p.StockLevel == null || p.StockLevel.OnHand - p.StockLevel.Reserved <= 0);
        }

        List<StockItemView> stock = await products
            .OrderBy(p => p.Sku)
            .Take(MaxRows)
            .Select(p => new StockItemView(
                p.Sku,
                p.Description,
                p.Uom,
                p.ListPrice,
                p.StockLevel == null ? 0 : p.StockLevel.OnHand,
                p.StockLevel == null ? 0 : p.StockLevel.Reserved,
                p.StockLevel == null ? 0 : p.StockLevel.OnHand - p.StockLevel.Reserved,
                p.StockLevel == null ? 0 : p.StockLevel.LeadTimeDays))
            .ToListAsync(cancellationToken);

        return stock;
    }

    /// <summary>Ordini ricevuti, dal più recente.</summary>
    public async Task<IReadOnlyList<OrderSummaryView>> ListOrdersAsync(OrderListFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<Order> query = db.Orders.AsNoTracking();

        if (filter.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        if (filter.CustomerId is { } customerId)
        {
            query = query.Where(o => o.CustomerId == customerId);
        }

        List<OrderSummaryView> orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Take(MaxRows)
            .Select(o => new OrderSummaryView(
                o.PublicId, o.OrderNumber, o.CustomerId, o.Customer.Name, o.Total, o.Status, o.ExternalRef,
                o.BackorderNote != null, o.CreatedAt))
            .ToListAsync(cancellationToken);

        return orders;
    }

    /// <summary>Dettaglio per numero d'ordine: è l'unico riferimento che il CRM conosce (G6.8).</summary>
    public async Task<OrderDetailView?> FindOrderAsync(string orderNumber, CancellationToken cancellationToken)
    {
        Order? order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.OrderNumber == orderNumber, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var summary = new OrderSummaryView(
            order.PublicId, order.OrderNumber, order.CustomerId, order.Customer.Name, order.Total, order.Status, order.ExternalRef,
            order.BackorderNote is not null, order.CreatedAt);

        return new OrderDetailView(
            summary,
            order.IdempotencyKey,
            order.BackorderNote,
            [.. order.Lines.OrderBy(l => l.Id).Select(l => new OrderLineView(l.Product.Sku, l.Product.Description, l.Product.Uom, l.Quantity, l.UnitPrice))]);
    }
}
