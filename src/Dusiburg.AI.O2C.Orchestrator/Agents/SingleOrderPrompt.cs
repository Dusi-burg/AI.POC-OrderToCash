namespace Dusiburg.AI.O2C.Orchestrator.Agents;

/// <summary>
/// Istruzioni dell'agente singolo (3.4), in inglese (G3.3), dalla specifica <c>SingleOrderAgent.agent.md</c> (D63).
/// </summary>
internal static class SingleOrderPrompt
{
    /// <summary>Sezione con la richiesta dell'esito strutturato a fine run.</summary>
    private const string OutcomeRequestSection = "Outcome request";

    /// <summary>Sezione usata solo quando la prima risposta non era un esito valido.</summary>
    private const string OutcomeRetrySection = "Outcome retry";

    private static readonly AgentSpec Spec = AgentSpec.Load(SingleOrderAgent.AgentName);

    public static string Instructions { get; } = Spec.Instructions;

    public static string OutcomeRequest { get; } = Spec.Section(OutcomeRequestSection);

    public static string OutcomeRetry { get; } = Spec.Section(OutcomeRetrySection);

    public static string Task(string dealId) => $"Process CRM deal {dealId}.";
}
