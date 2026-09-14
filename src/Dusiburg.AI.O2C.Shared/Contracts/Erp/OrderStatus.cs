using System.Text.Json.Serialization;
using Dusiburg.AI.O2C.Shared.Serialization;

namespace Dusiburg.AI.O2C.Shared.Contracts.Erp;

/// <summary>
/// Stato dell'ordine ERP. Valori espliciti: sono le PK della tabella di lookup <c>erp.OrderStatus</c> (tinyint).
/// </summary>
[JsonConverter(typeof(StrictStringEnumConverter<OrderStatus>))]
public enum OrderStatus : byte
{
    /// <summary>Tutte le righe erano disponibili alla creazione.</summary>
    Confirmed = 1,

    /// <summary>Almeno una riga non era disponibile: l'ordine è stato approvato in backorder (D20).</summary>
    Backorder = 2
}
