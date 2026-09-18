using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Agents;

/// <summary>
/// Filtro della catena fra agenti (D62): prima di ogni richiesta al modello toglie il <b>testo</b> dei messaggi scritti da
/// altri agenti del workflow, lasciando le loro chiamate ai tool e i risultati, che sono i fatti su cui l'agente lavora.
/// <para>
/// Nel workflow di handoff il testo con cui un agente accompagna il passaggio di mano arriva al successivo come messaggio
/// dell'utente. Con qwen3.5:9b alcune formulazioni di IntakeAgent ("…I'll hand off to FulfillmentAgent immediately.")
/// facevano passare la mano a FulfillmentAgent senza verificare le giacenze, in modo sistematico (replay 20/20). Le
/// istruzioni non bastano: quel testo lo produce un altro modello e cambia a ogni run.
/// </para>
/// <para>
/// Agisce solo su ciò che viene inviato al modello: la conversazione del workflow e i checkpoint restano completi.
/// </para>
/// </summary>
public sealed class ForeignAgentTextFilter(IChatClient inner, string agentName, IReadOnlySet<string> workflowAgentNames)
    : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Filter(messages, agentName, workflowAgentNames), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Filter(messages, agentName, workflowAgentNames), options, cancellationToken);

    /// <summary>
    /// Copia della conversazione senza il testo degli altri agenti; un messaggio che resta vuoto si toglie. I messaggi
    /// senza autore (la richiesta iniziale, le continuazioni del framework) e quelli dell'agente stesso restano intatti.
    /// </summary>
    public static List<ChatMessage> Filter(IEnumerable<ChatMessage> messages, string agentName, IReadOnlySet<string> workflowAgentNames)
    {
        var filtered = new List<ChatMessage>();

        foreach (var message in messages)
        {
            if (message.AuthorName is not { } author || author == agentName || !workflowAgentNames.Contains(author))
            {
                filtered.Add(message);

                continue;
            }

            List<AIContent> kept = [.. message.Contents.Where(content => content is not TextContent)];

            if (kept.Count == 0)
            {
                continue;
            }

            var copy = message.Clone();
            copy.Contents = kept;
            filtered.Add(copy);
        }

        return filtered;
    }
}
