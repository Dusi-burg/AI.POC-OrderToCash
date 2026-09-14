using System.Text.Json.Serialization;
using Dusiburg.AI.O2C.Shared.Serialization;

namespace Dusiburg.AI.O2C.Shared.Contracts.Crm;

/// <summary>
/// Stato O2C scritto sul deal da <c>update_deal</c>: enum chiuso (D23, M4).
/// </summary>
[JsonConverter(typeof(StrictStringEnumConverter<DealStatus>))]
public enum DealStatus
{
    ApprovalPending = 1,
    OrderCreated = 2,
    Rejected = 3,
    Expired = 4,
    Discarded = 5,
    Failed = 6
}
