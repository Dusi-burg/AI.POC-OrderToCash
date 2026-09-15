using Dusiburg.AI.O2C.Orchestrator.Model;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Support;

/// <summary>Modello a copione (3.8): ogni richiesta consuma il passo successivo; nessuna chiamata reale.</summary>
internal sealed class ScriptedChatClient(params Func<IReadOnlyList<ChatMessage>, ChatResponse>[] steps) : IChatClient
{
    private int _next;

    public List<ChatOptions?> Options { get; } = [];

    public int RequestCount => _next;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Options.Add(options);

        if (_next >= steps.Length)
        {
            throw new InvalidOperationException("Copione del modello esaurito.");
        }

        return Task.FromResult(steps[_next++](messages.ToList()));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Il copione non supporta lo streaming.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static ChatResponse Call(string callId, string toolName, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, toolName, arguments)]));

    public static ChatResponse Text(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));
}

internal sealed class ScriptedModelClientFactory(IChatClient chatClient) : IModelClientFactory
{
    public ModelClient Create() => new(chatClient, "stub", "scripted", new ChatOptions());
}

internal sealed class InMemoryToolCatalog(params AgentTool[] tools) : IToolCatalog
{
    public Task<IReadOnlyList<AgentTool>> GetToolsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentTool>>(tools);
}
