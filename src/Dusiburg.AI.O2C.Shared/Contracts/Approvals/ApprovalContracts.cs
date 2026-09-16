using System.Text.Json.Serialization;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Serialization;

namespace Dusiburg.AI.O2C.Shared.Contracts.Approvals;

/// <summary>
/// Regola di §7 che ha reso necessaria l'approvazione. Valori espliciti: sono le PK della lookup <c>orch.ApprovalReason</c> (tinyint).
/// </summary>
[JsonConverter(typeof(StrictStringEnumConverter<ApprovalReason>))]
public enum ApprovalReason : byte
{
    /// <summary>Totale dell'ordine oltre <c>APPROVAL_THRESHOLD_EUR</c> (confronto stretto).</summary>
    OverThreshold = 1,

    /// <summary>Almeno una riga con <c>available = false</c>: l'ordine andrebbe in backorder (D20).</summary>
    InsufficientStock = 2,

    /// <summary>Cliente non presente in ERP: l'anagrafica è stata creata durante questo run.</summary>
    NewCustomer = 3,

    /// <summary>Cliente con <c>isBlocked = true</c>: l'approvazione è l'unica strada (§7).</summary>
    BlockedCustomer = 4
}

/// <summary>
/// Esito della decisione su una richiesta di approvazione. Valori espliciti: PK della lookup <c>orch.ApprovalStatus</c> (tinyint).
/// </summary>
[JsonConverter(typeof(StrictStringEnumConverter<ApprovalStatus>))]
public enum ApprovalStatus : byte
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Expired = 4
}

/// <summary>Riga della proposta di ordine mostrata all'approvatore, con l'esito della verifica di giacenza.</summary>
public sealed record ApprovalLine(string Sku, int Quantity, decimal UnitPrice, bool? Available, int? OnHand, int? LeadTimeDays)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

/// <summary>
/// Proposta di ordine congelata al momento della sospensione (<c>ApprovalRequest.PayloadJson</c>, §7): è ciò che
/// l'approvatore vede e, in caso di approvazione, ciò che il workflow ripreso realizza.
/// </summary>
public sealed record ApprovalPayload(
    string DealId,
    int DealRevision,
    string DealName,
    string CompanyId,
    string CompanyName,
    int CustomerId,
    string CustomerName,
    bool CustomerIsBlocked,
    bool CustomerCreatedInThisRun,
    IReadOnlyList<ApprovalLine> Lines,
    decimal Total,
    string IdempotencyKey);

/// <summary>Decisione inviata all'endpoint di callback di <c>Approvals.Web</c> (usato dalla UI e, in futuro, da Teams).</summary>
public sealed record ApprovalDecisionRequest(bool Approved, string? Note);

/// <summary>Esito della decisione restituito dal callback.</summary>
public sealed record ApprovalDecisionResponse(Guid ApprovalId, ApprovalStatus Status, DateTimeOffset DecidedAt, string DecidedBy);

/// <summary>Riepilogo di una richiesta per l'elenco e il dettaglio della UI.</summary>
public sealed record ApprovalSummary(
    Guid ApprovalId,
    string DealId,
    string CorrelationId,
    ApprovalStatus Status,
    IReadOnlyList<ApprovalReason> Reasons,
    decimal Total,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    string? DecisionNote);

/// <summary>Righe di <c>create_order</c> ricostruite dalla proposta approvata.</summary>
public static class ApprovalPayloadExtensions
{
    public static IReadOnlyList<OrderLineInput> ToOrderLines(this ApprovalPayload payload) =>
        [.. payload.Lines.Select(l => new OrderLineInput(l.Sku, l.Quantity, l.UnitPrice))];
}
