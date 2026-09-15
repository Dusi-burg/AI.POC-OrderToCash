namespace Dusiburg.AI.O2C.Orchestrator.Agents;

/// <summary>Modalità di elaborazione di un deal (D47), da <c>O2C_AGENT_MODE</c>.</summary>
public static class AgentModes
{
    public const string Setting = "O2C_AGENT_MODE";

    /// <summary>Workflow Intake → Fulfillment → Order con handoff (default).</summary>
    public const string Multi = "multi";

    /// <summary>Agente singolo della Fase 3, per confronto.</summary>
    public const string Single = "single";

    public static string FromConfiguration(IConfiguration configuration)
    {
        var mode = (configuration[Setting] ?? Multi).Trim().ToLowerInvariant();

        return mode is Multi or Single
            ? mode
            : throw new InvalidOperationException($"{Setting} '{mode}' non supportato: usare '{Multi}' oppure '{Single}'.");
    }
}

/// <summary>Chi porta un deal fino all'esito: il workflow a tre agenti o l'agente singolo.</summary>
public interface IDealAgent
{
    string Mode { get; }

    Task<DealProcessingResult> ProcessAsync(string dealId, string correlationId, CancellationToken cancellationToken);
}
