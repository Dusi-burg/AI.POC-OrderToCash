using System.Text.Json.Serialization;
using Dusiburg.AI.O2C.Shared.Serialization;

namespace Dusiburg.AI.O2C.Shared.Contracts.Crm;

/// <summary>
/// Stage commerciale del deal nel CRM mock. Valori espliciti: sono le PK della tabella di lookup <c>crm.DealStage</c> (tinyint).
/// Il contratto <c>get_deal</c> lo espone per nome; le viste di <c>Crm.Web</c> (Fase 6) lo usano tipizzato.
/// </summary>
[JsonConverter(typeof(StrictStringEnumConverter<DealStage>))]
public enum DealStage : byte
{
    ContractSent = 1,
    ClosedWon = 2,
    ClosedLost = 3
}
