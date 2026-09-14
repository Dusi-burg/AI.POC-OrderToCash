using System.Text.Json.Serialization;
using O2C.Shared.Serialization;

namespace O2C.Shared.Contracts.Crm;

/// <summary>
/// Stato O2C scritto sul deal da <c>update_deal</c>: enum chiuso (D23, M4).
/// </summary>
[JsonConverter(typeof(StrictStringEnumConverter<DealStatus>))]
public enum DealStatus
{
    ApprovalPending,
    OrderCreated,
    Rejected,
    Expired,
    Discarded,
    Failed
}
