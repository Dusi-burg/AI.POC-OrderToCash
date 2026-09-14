using System.Text.Json.Serialization;
using Dusiburg.AI.O2C.Shared.Serialization;

namespace Dusiburg.AI.O2C.Shared.Contracts.Crm;

/// <summary>
/// Stato O2C scritto sul deal da <c>update_deal</c>: enum chiuso (D23, M4).
/// Valori espliciti: sono le PK della tabella di lookup <c>crm.DealStatus</c> (tinyint).
/// </summary>
[JsonConverter(typeof(StrictStringEnumConverter<DealStatus>))]
public enum DealStatus : byte
{
    ApprovalPending = 2,
    OrderCreated = 1,
    Rejected = 3,
    Expired = 4,
    Discarded = 5,
    Failed = 10
}
