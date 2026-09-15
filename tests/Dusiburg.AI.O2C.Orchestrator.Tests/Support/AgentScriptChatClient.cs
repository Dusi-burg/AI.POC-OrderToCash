using System.Runtime.CompilerServices;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Support;

/// <summary>
/// Modello a copione per il workflow a tre agenti: riconosce l'agente dalle istruzioni e consuma il suo copione;
/// a copione finito risponde con un testo. Registra i tool offerti a ogni agente.
/// </summary>
internal sealed class AgentScriptChatClient(Dictionary<string, Queue<ChatResponse>> scripts) : IChatClient
{
    private readonly Lock _gate = new();

    public List<(string Agent, IReadOnlyList<string> Tools)> Requests { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var agent = WorkflowAgents.All.FirstOrDefault(a => options?.Instructions?.Contains($"You are {a.Name}", StringComparison.Ordinal) == true)?.Name ?? "?";

        lock (_gate)
        {
            Requests.Add((agent, options?.Tools?.Select(t => t.Name).ToList() ?? []));

            return Task.FromResult(scripts.TryGetValue(agent, out var script) && script.Count > 0
                ? script.Dequeue()
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static Queue<ChatResponse> Script(params ChatResponse[] steps) => new(steps);
}
