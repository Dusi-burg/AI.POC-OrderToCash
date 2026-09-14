using Dusiburg.AI.O2C.Shared.Contracts.Erp;

namespace Dusiburg.AI.O2C.Erp.Data.Entities;

public sealed class Customer
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Univoca quando valorizzata (indice filtrato).</summary>
    public string? VatNumber { get; set; }

    public required string Email { get; set; }

    public required string Address { get; set; }

    public decimal CreditLimit { get; set; }

    /// <summary>Informativo per la policy dell'orchestratore: l'ERP non rifiuta gli ordini (G1.5).</summary>
    public bool IsBlocked { get; set; }

    public List<Order> Orders { get; set; } = [];
}

public sealed class Product
{
    public int Id { get; set; }

    /// <summary>Chiave di business (es. <c>IND-BRG-001</c>), univoca.</summary>
    public required string Sku { get; set; }

    public required string Description { get; set; }

    /// <summary>Informativo: sull'ordine vale il prezzo del deal (D21).</summary>
    public decimal ListPrice { get; set; }

    public required string Uom { get; set; }

    public StockLevel? StockLevel { get; set; }
}

/// <summary>Giacenza di un prodotto (1:1): disponibile se <c>OnHand − Reserved ≥ quantity</c> (D21).</summary>
public sealed class StockLevel
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int OnHand { get; set; }

    public int Reserved { get; set; }

    public int LeadTimeDays { get; set; }

    /// <summary>Concorrenza ottimistica sulla riserva dello stock.</summary>
    public byte[] RowVersion { get; set; } = null!;
}

public sealed class Order
{
    public int Id { get; set; }

    /// <summary>
    /// Identificativo pubblico (GUID generato dal DB, univoco): è l'<c>orderId</c> dei contratti §6.
    /// La PK resta numerica.
    /// </summary>
    public Guid PublicId { get; set; }

    /// <summary><c>SO-2026-000001</c>, dalla sequence <c>erp.OrderNumberSeq</c> (G1.2).</summary>
    public required string OrderNumber { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public decimal Total { get; set; }

    /// <summary>Colonna <c>OrderStatusId</c>, FK verso <c>erp.OrderStatus</c>.</summary>
    public OrderStatus Status { get; set; }

    /// <summary>Il dealId del CRM.</summary>
    public required string ExternalRef { get; set; }

    /// <summary>Univoca: impedisce ordini doppi a fronte di retry (§8, §12).</summary>
    public required string IdempotencyKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    /// <summary>Prezzo unitario del deal (D21).</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Riga di una tabella di lookup generata da un enum: <see cref="Id"/> è il valore numerico esplicito nel codice,
/// <see cref="Name"/> il nome del membro. Le righe vengono da <c>Enum.GetValues</c>, mai scritte a mano.
/// </summary>
public sealed class EnumLookup<TEnum> where TEnum : struct, Enum
{
    public TEnum Id { get; set; }

    public required string Name { get; set; }
}
