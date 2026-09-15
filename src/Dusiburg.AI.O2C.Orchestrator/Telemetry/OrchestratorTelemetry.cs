using System.Diagnostics;
using Dusiburg.AI.O2C.Shared.Telemetry;

namespace Dusiburg.AI.O2C.Orchestrator.Telemetry;

internal static class OrchestratorTelemetry
{
    /// <summary>Span radice di un deal elaborato (3.5); i tool e le chiamate MCP → Erp.Api stanno sotto.</summary>
    public const string ProcessDealActivityName = "o2c.process_deal";

    public static readonly ActivitySource Source = new(O2CTelemetry.Sources.Orchestrator);
}
