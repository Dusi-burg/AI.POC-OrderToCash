using System.Diagnostics;
using Dusiburg.AI.O2C.Shared.Telemetry;

namespace Dusiburg.AI.O2C.Orchestrator.Telemetry;

internal static class OrchestratorTelemetry
{
    /// <summary>Span radice di un deal elaborato (3.5); i tool e le chiamate MCP → Erp.Api stanno sotto.</summary>
    public const string ProcessDealActivityName = "o2c.process_deal";

    /// <summary>Il workflow si sospende su una richiesta di approvazione (5.7).</summary>
    public const string ApprovalRequestedActivityName = "approval.requested";

    /// <summary>Ripresa del workflow dopo la decisione: ha come parent il <c>TraceParent</c> salvato (G5.4).</summary>
    public const string ApprovalResumeActivityName = "approval.resume";

    /// <summary>Scadenza di una richiesta rimasta pendente (5.6).</summary>
    public const string ApprovalExpiredActivityName = "approval.expired";

    public static readonly ActivitySource Source = new(O2CTelemetry.Sources.Orchestrator);
}
