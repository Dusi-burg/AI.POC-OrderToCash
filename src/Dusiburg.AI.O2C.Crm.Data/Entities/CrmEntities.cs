using Dusiburg.AI.O2C.Shared.Contracts.Crm;

namespace Dusiburg.AI.O2C.Crm.Data.Entities;

public sealed class Company
{
    public int Id { get; set; }

    /// <summary>Chiave di business (es. <c>C-01</c>), univoca: è il <c>companyId</c> dei contratti §6.2.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? VatNumber { get; set; }

    public required string Email { get; set; }

    public required string Address { get; set; }

    public List<Deal> Deals { get; set; } = [];
}

public sealed class Deal
{
    public int Id { get; set; }

    /// <summary>Chiave di business (es. <c>D-1001</c>), univoca: è il <c>dealId</c> dei contratti §6.2.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    /// <summary>Colonna <c>DealStageId</c>, FK verso <c>crm.DealStage</c>.</summary>
    public DealStage Stage { get; set; }

    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    /// <summary>Cresce solo con le modifiche commerciali, non con gli aggiornamenti di O2C (G1.3, M3).</summary>
    public int Revision { get; set; }

    public string? ErpOrderNumber { get; set; }

    /// <summary>
    /// Ultimo stato scritto da <c>update_deal</c>; null finché O2C non ha agito.
    /// Colonna <c>DealStatusId</c>, FK verso <c>crm.DealStatus</c>.
    /// </summary>
    public DealStatus? O2CStatus { get; set; }

    public string? LastNote { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<DealLineItem> LineItems { get; set; } = [];

    public List<DealNote> Notes { get; set; } = [];
}

public sealed class DealLineItem
{
    public int Id { get; set; }

    public int DealId { get; set; }

    public Deal Deal { get; set; } = null!;

    /// <summary>SKU come scritto nel CRM: nessuna FK verso l'ERP (sistemi separati, D-1007 ne usa uno inesistente).</summary>
    public required string Sku { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

/// <summary>Storico delle scritture di O2C sul deal, per audit.</summary>
public sealed class DealNote
{
    public int Id { get; set; }

    public int DealId { get; set; }

    public Deal Deal { get; set; } = null!;

    /// <summary>Colonna <c>DealStatusId</c>, FK verso <c>crm.DealStatus</c>.</summary>
    public DealStatus Status { get; set; }

    public string? ErpOrderNumber { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
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
