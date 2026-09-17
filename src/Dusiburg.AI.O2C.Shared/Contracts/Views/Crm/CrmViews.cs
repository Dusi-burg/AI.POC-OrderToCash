using System.Text.Json.Serialization;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Serialization;

namespace Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;

// Viste del CRM mock per Crm.Web (Fase 6, G6.1-G6.2): esposte da GET /api/views su Crm.Mcp, separate dai contratti dei tool di §6.2.

public sealed record CompanySummaryView(string CompanyId, string Name, string? VatNumber, string Email, int DealCount);

public sealed record CompanyDetailView(
    string CompanyId,
    string Name,
    string? VatNumber,
    string Email,
    string Address,
    IReadOnlyList<DealSummaryView> Deals);

public sealed record DealSummaryView(
    string DealId,
    string Name,
    string CompanyId,
    string CompanyName,
    decimal Amount,
    string Currency,
    DealStage Stage,
    int Revision,
    DealStatus? O2CStatus,
    string? ErpOrderNumber,
    DateTimeOffset UpdatedAt);

public sealed record DealLineView(string Sku, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

/// <summary>Scrittura di O2C sul deal (storico di <c>update_deal</c>), in ordine di arrivo.</summary>
public sealed record DealNoteView(DealStatus Status, string? ErpOrderNumber, string? Note, DateTimeOffset CreatedAt);

public sealed record DealDetailView(
    DealSummaryView Deal,
    string? LastNote,
    IReadOnlyList<DealLineView> Lines,
    IReadOnlyList<DealNoteView> Notes);

/// <summary>Esito del comando di chiusura (D59): solo <see cref="Won"/> avvia il flusso O2C.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<DealCloseOutcome>))]
public enum DealCloseOutcome
{
    Won = 1,
    Lost = 2
}

/// <summary>Input di <c>POST /api/deals/{dealId}/close</c>.</summary>
public sealed record CloseDealRequest(DealCloseOutcome Outcome);

/// <summary>
/// Stati O2C finali: la pagina del deal smette di aggiornarsi da sola (G6.7). <see cref="DealStatus.ApprovalPending"/>
/// non lo è, perché la decisione può arrivare in qualunque momento.
/// </summary>
public static class DealStatusExtensions
{
    public static bool IsTerminal(this DealStatus status) => status != DealStatus.ApprovalPending;
}
