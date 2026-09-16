using System.Globalization;
using Dusiburg.AI.O2C.Erp.Api.Data;
using Dusiburg.AI.O2C.Erp.Data;
using Dusiburg.AI.O2C.Erp.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Erp.Api.Orders;

internal enum CreateOrderOutcome
{
    Created,
    Existing,
    Invalid,
    NotFound
}

internal sealed record CreateOrderResult(CreateOrderOutcome Outcome, CreateOrderResponse? Order = null, string? Error = null);

/// <summary>
/// <c>create_order</c> idempotente (1.5, §12): a parità di <c>IdempotencyKey</c> restituisce sempre lo stesso ordine,
/// anche con richieste concorrenti; la garanzia finale è l'indice univoco su <c>erp.Order.IdempotencyKey</c>.
/// </summary>
internal sealed class OrderService(ErpDbContext db, TimeProvider timeProvider, ILogger<OrderService> logger)
{
    public const int MaxLines = 100;

    public async Task<CreateOrderResult> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var error = Validate(request);

        if (error is not null)
        {
            return new CreateOrderResult(CreateOrderOutcome.Invalid, Error: error);
        }

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(() => CreateOnceAsync(request, cancellationToken));
        }
        catch (DbUpdateException exception) when (SqlErrors.IsUniqueViolation(exception))
        {
            // Una richiesta concorrente con la stessa chiave ha inserito l'ordine per prima: si restituisce il suo.
            db.ChangeTracker.Clear();

            var existing = await FindByKeyAsync(request.IdempotencyKey, cancellationToken);

            if (existing is null)
            {
                throw;
            }

            logger.LogInformation("Ordine {OrderNumber} già creato da una richiesta concorrente con la stessa chiave", existing.OrderNumber);

            return new CreateOrderResult(CreateOrderOutcome.Existing, existing);
        }
    }

    public async Task<OrderDto?> GetAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .SingleOrDefaultAsync(o => o.PublicId == orderId, cancellationToken);

        return order is null
            ? null
            : new OrderDto(
                order.PublicId,
                order.OrderNumber,
                order.CustomerId,
                order.Total,
                order.Status,
                order.ExternalRef,
                order.IdempotencyKey,
                order.CreatedAt,
                order.Lines.OrderBy(l => l.Id).Select(l => new OrderLineDto(l.Id, l.Product.Sku, l.Quantity, l.UnitPrice)).ToList(),
                order.BackorderNote);
    }

    private async Task<CreateOrderResult> CreateOnceAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        var existing = await FindByKeyAsync(request.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            return new CreateOrderResult(CreateOrderOutcome.Existing, existing);
        }

        if (!await db.Customers.AnyAsync(c => c.Id == request.CustomerId, cancellationToken))
        {
            return new CreateOrderResult(CreateOrderOutcome.NotFound, Error: $"Cliente {request.CustomerId} non trovato.");
        }

        var skus = request.Lines.Select(l => l.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var products = await db.Products
            .AsNoTracking()
            .Include(p => p.StockLevel)
            .Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var missing = skus.Where(s => !products.ContainsKey(s)).ToList();

        if (missing.Count > 0)
        {
            return new CreateOrderResult(CreateOrderOutcome.NotFound, Error: $"SKU non trovati: {string.Join(", ", missing)}.");
        }

        // Quantità richiesta per prodotto sull'intero ordine: lo stesso SKU può comparire in più righe.
        var requested = request.Lines
            .GroupBy(l => products[l.Sku])
            .Select(g => (Product: g.Key, Quantity: g.Sum(l => l.Quantity)))
            .ToList();

        var allAvailable = requested.All(r => r.Product.StockLevel is { } stock && stock.OnHand - stock.Reserved >= r.Quantity);

        // Cosa manca, prima di riservare: la riserva viene fatta comunque (D21), quindi dopo non sarebbe più ricavabile.
        var backorderNote = BackorderNote(requested);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var sequence = await db.Database
            .SqlQuery<long>($"SELECT NEXT VALUE FOR [erp].[OrderNumberSeq] AS [Value]")
            .ToListAsync(cancellationToken);

        var createdAt = timeProvider.GetUtcNow();

        var order = new Order
        {
            OrderNumber = string.Create(CultureInfo.InvariantCulture, $"SO-{createdAt.Year}-{sequence[0]:000000}"),
            CustomerId = request.CustomerId,
            Total = request.Lines.Sum(l => l.Quantity * l.UnitPrice),
            Status = allAvailable ? OrderStatus.Confirmed : OrderStatus.Backorder,
            ExternalRef = request.ExternalRef,
            IdempotencyKey = request.IdempotencyKey,
            BackorderNote = backorderNote,
            CreatedAt = createdAt,
            Lines = request.Lines
                .Select(l => new OrderLine { ProductId = products[l.Sku].Id, Quantity = l.Quantity, UnitPrice = l.UnitPrice })
                .ToList()
        };

        db.Orders.Add(order);

        await db.SaveChangesAsync(cancellationToken);

        // Riserva dello stock nella stessa transazione (D21), con un UPDATE atomico per prodotto.
        foreach (var (product, quantity) in requested)
        {
            await db.StockLevels
                .Where(s => s.ProductId == product.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved + quantity), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Creato ordine {OrderNumber} ({OrderStatus}) per {ExternalRef} con chiave {IdempotencyKey}",
            order.OrderNumber, order.Status, order.ExternalRef, order.IdempotencyKey);

        return new CreateOrderResult(
            CreateOrderOutcome.Created,
            new CreateOrderResponse(order.PublicId, order.OrderNumber, order.Total, order.Status, order.BackorderNote));
    }

    /// <summary>
    /// Righe da approvvigionare, in chiaro: quantità richiesta meno quella davvero disponibile
    /// (<c>OnHand − Reserved</c>, mai negativa). <c>null</c> se l'ordine è interamente coperto.
    /// </summary>
    private static string? BackorderNote(IReadOnlyList<(Product Product, int Quantity)> requested)
    {
        var missing = requested
            .Select(r => (r.Product, Shortfall: r.Quantity - Math.Max(0, (r.Product.StockLevel?.OnHand ?? 0) - (r.Product.StockLevel?.Reserved ?? 0))))
            .Where(r => r.Shortfall > 0)
            .Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Product.Sku}: {r.Shortfall} {r.Product.Uom} da ordinare"))
            .ToList();

        return missing.Count == 0 ? null : string.Join("; ", missing);
    }

    private async Task<CreateOrderResponse?> FindByKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        await db.Orders
            .AsNoTracking()
            .Where(o => o.IdempotencyKey == idempotencyKey)
            .Select(o => new CreateOrderResponse(o.PublicId, o.OrderNumber, o.Total, o.Status, o.BackorderNote))
            .SingleOrDefaultAsync(cancellationToken);

    private static string? Validate(CreateOrderRequest request)
    {
        if (request.CustomerId <= 0)
        {
            return "customerId è obbligatorio.";
        }

        if (string.IsNullOrWhiteSpace(request.ExternalRef) || request.ExternalRef.Length > 50)
        {
            return "externalRef è obbligatorio (max 50 caratteri).";
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
        {
            return "idempotencyKey è obbligatoria (max 100 caratteri).";
        }

        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Count > MaxLines)
        {
            return $"L'ordine deve avere da 1 a {MaxLines} righe.";
        }

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];

            if (line is null || string.IsNullOrWhiteSpace(line.Sku) || line.Sku.Length > 50)
            {
                return $"Riga {i + 1}: sku è obbligatorio (max 50 caratteri).";
            }

            if (line.Quantity <= 0)
            {
                return $"Riga {i + 1}: quantity deve essere maggiore di zero.";
            }

            if (line.UnitPrice < 0 || decimal.Round(line.UnitPrice, 2) != line.UnitPrice)
            {
                return $"Riga {i + 1}: unitPrice deve essere non negativo, con al massimo due decimali.";
            }
        }

        return null;
    }
}
