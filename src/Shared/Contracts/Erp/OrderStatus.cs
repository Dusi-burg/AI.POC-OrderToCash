using System.Text.Json.Serialization;
using O2C.Shared.Serialization;

namespace O2C.Shared.Contracts.Erp;

[JsonConverter(typeof(StrictStringEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    /// <summary>Tutte le righe erano disponibili alla creazione.</summary>
    Confirmed,

    /// <summary>Almeno una riga non era disponibile: l'ordine è stato approvato in backorder (D20).</summary>
    Backorder
}
