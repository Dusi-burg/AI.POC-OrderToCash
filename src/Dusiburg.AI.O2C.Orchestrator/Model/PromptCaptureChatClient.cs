using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Model;

/// <summary>
/// Diagnostica: salva su file ogni richiesta al modello così come esce dall'orchestratore — istruzioni, messaggi della
/// conversazione, tool offerti con la loro descrizione, opzioni — e la risposta ricevuta. Si attiva solo con
/// <see cref="DirectoryConfigurationKey"/>; i file possono contenere dati di business, quindi vanno tenuti fuori dal repo.
/// </summary>
public sealed partial class PromptCaptureChatClient(IChatClient inner, string directory) : DelegatingChatClient(inner)
{
    public const string DirectoryConfigurationKey = "O2C_PROMPT_CAPTURE_DIR";

    private static readonly JsonSerializerOptions Output = new(AIJsonUtilities.DefaultOptions) { WriteIndented = true };

    private static int _sequence;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> request = [.. messages];
        ChatResponse response = await base.GetResponseAsync(request, options, cancellationToken);

        await WriteAsync(request, options, response, cancellationToken);

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ChatMessage> request = [.. messages];
        List<ChatResponseUpdate> updates = [];

        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(request, options, cancellationToken))
        {
            updates.Add(update);

            yield return update;
        }

        await WriteAsync(request, options, updates.ToChatResponse(), cancellationToken);
    }

    private async Task WriteAsync(IReadOnlyList<ChatMessage> request, ChatOptions? options, ChatResponse response, CancellationToken cancellationToken)
    {
        int sequence = Interlocked.Increment(ref _sequence);
        string agent = AgentName(options?.Instructions);

        var capture = new JsonObject
        {
            ["sequence"] = sequence,
            ["capturedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["agent"] = agent,
            ["options"] = new JsonObject
            {
                ["modelId"] = options?.ModelId,
                ["temperature"] = options?.Temperature,
                ["toolMode"] = options?.ToolMode?.ToString(),
                ["allowMultipleToolCalls"] = options?.AllowMultipleToolCalls,
                ["additionalProperties"] = options?.AdditionalProperties is { } extra
                    ? JsonSerializer.SerializeToNode(extra.ToDictionary(p => p.Key, p => p.Value?.ToString()), Output)
                    : null
            },
            ["instructions"] = options?.Instructions,
            ["tools"] = new JsonArray([.. (options?.Tools ?? []).Select(DescribeTool)]),
            ["messages"] = new JsonArray([.. request.Select(DescribeMessage)]),
            ["response"] = new JsonArray([.. response.Messages.Select(DescribeMessage)])
        };

        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, $"{sequence:D3}-{agent}.json");

        await File.WriteAllTextAsync(path, capture.ToJsonString(Output), cancellationToken);
    }

    private static JsonNode DescribeTool(AITool tool) => tool switch
    {
        AIFunctionDeclaration function => new JsonObject
        {
            ["name"] = function.Name,
            ["description"] = function.Description,
            ["schema"] = JsonNode.Parse(function.JsonSchema.GetRawText())
        },
        _ => new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["kind"] = tool.GetType().Name }
    };

    private static JsonNode DescribeMessage(ChatMessage message) => new JsonObject
    {
        ["role"] = message.Role.Value,
        ["authorName"] = message.AuthorName,
        ["contents"] = new JsonArray([.. message.Contents.Select(DescribeContent)])
    };

    private static JsonNode DescribeContent(AIContent content) => content switch
    {
        TextContent text => new JsonObject { ["text"] = text.Text },
        FunctionCallContent call => new JsonObject
        {
            ["functionCall"] = call.Name,
            ["callId"] = call.CallId,
            ["arguments"] = JsonSerializer.SerializeToNode(call.Arguments, Output)
        },
        // Il tool sensibile arriva già come richiesta di approvazione: si salva la chiamata che contiene.
        ToolApprovalRequestContent { ToolCall: FunctionCallContent call } => new JsonObject
        {
            ["approvalRequest"] = call.Name,
            ["callId"] = call.CallId,
            ["arguments"] = JsonSerializer.SerializeToNode(call.Arguments, Output)
        },
        FunctionResultContent result => new JsonObject
        {
            ["functionResult"] = result.CallId,
            ["result"] = JsonSerializer.SerializeToNode(result.Result, Output)
        },
        _ => new JsonObject { ["content"] = content.GetType().Name }
    };

    /// <summary>Nome dell'agente dalla prima riga delle istruzioni ("You are FulfillmentAgent …"), per nominare i file.</summary>
    private static string AgentName(string? instructions) =>
        instructions is not null && AgentNamePattern().Match(instructions) is { Success: true } match ? match.Groups[1].Value : "unknown";

    [GeneratedRegex(@"You are (\w+)")]
    private static partial Regex AgentNamePattern();
}
