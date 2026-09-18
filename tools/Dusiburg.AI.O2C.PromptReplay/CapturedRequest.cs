using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.PromptReplay;

/// <summary>Un caso del manifesto <c>Cases/cases.json</c>: una richiesta catturata e la prima chiamata che ci si aspetta.</summary>
public sealed record ReplayCase(string Name, string Capture, string ExpectedFirstCall, string? Description = null, string? ReplaceLastText = null);

/// <summary>
/// Richiesta al modello salvata da <c>PromptCaptureChatClient</c> (<c>O2C_PROMPT_CAPTURE_DIR</c>), ricostruita come
/// messaggi, tool e istruzioni di <c>Microsoft.Extensions.AI</c>.
/// </summary>
public sealed record CapturedRequest(string Agent, string Instructions, IReadOnlyList<ChatMessage> Messages, IReadOnlyList<AITool> Tools, bool? AllowMultipleToolCalls)
{
    /// <summary>Inizio del paragrafo che Agent Framework aggiunge alle istruzioni di ogni agente del workflow di handoff.</summary>
    private const string FrameworkHandoffInstructionsStart = "You are one agent in a multi-agent system.";

    public static CapturedRequest Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        List<ChatMessage> messages = [.. root.GetProperty("messages").EnumerateArray().Select(ReadMessage)];
        List<AITool> tools = [.. root.GetProperty("tools").EnumerateArray().Select(ReadTool)];
        JsonElement allowMultiple = root.GetProperty("options").GetProperty("allowMultipleToolCalls");

        return new CapturedRequest(
            root.GetProperty("agent").GetString()!,
            root.GetProperty("instructions").GetString() ?? string.Empty,
            messages,
            tools,
            allowMultiple.ValueKind is JsonValueKind.True or JsonValueKind.False ? allowMultiple.GetBoolean() : null);
    }

    /// <summary>
    /// La stessa richiesta con le istruzioni e le descrizioni degli handoff <b>attuali</b> dell'agente: è ciò che serve per
    /// verificare un cambio di istruzioni su conversazioni già viste. Il paragrafo del framework resta quello catturato.
    /// </summary>
    public CapturedRequest WithCurrentAgentDefinition()
    {
        AgentDefinition definition = WorkflowAgents.All.Single(a => a.Name == Agent);
        int frameworkStart = Instructions.IndexOf(FrameworkHandoffInstructionsStart, StringComparison.Ordinal);
        string frameworkInstructions = frameworkStart >= 0 ? Instructions[frameworkStart..] : string.Empty;
        string instructions = frameworkInstructions.Length > 0 ? $"{definition.Instructions}\n{frameworkInstructions}" : definition.Instructions;

        string? handoffCondition = Agent == WorkflowAgents.Intake.Name ? WorkflowAgents.IntakeHandoffCondition
            : Agent == WorkflowAgents.Fulfillment.Name ? WorkflowAgents.FulfillmentHandoffCondition
            : null;

        List<AITool> tools = [.. Tools.Select(tool =>
            handoffCondition is not null && tool is AIFunctionDeclaration declaration && declaration.Name.StartsWith("handoff_to", StringComparison.Ordinal)
                ? AIFunctionFactory.CreateDeclaration(declaration.Name, handoffCondition, declaration.JsonSchema)
                : tool)];

        return this with { Instructions = instructions, Tools = tools };
    }

    /// <summary>Sostituisce il primo testo non vuoto dell'ultimo messaggio (per provare formulazioni diverse).</summary>
    public CapturedRequest WithLastText(string text)
    {
        List<ChatMessage> messages = [.. Messages.Select(m => m.Clone())];
        ChatMessage last = messages[^1];
        int index = last.Contents.ToList().FindIndex(c => c is TextContent { Text.Length: > 0 });

        if (index < 0)
        {
            throw new InvalidOperationException("L'ultimo messaggio catturato non ha testo da sostituire.");
        }

        List<AIContent> contents = [.. last.Contents];
        contents[index] = new TextContent(text);
        last.Contents = contents;

        return this with { Messages = messages };
    }

    private static ChatMessage ReadMessage(JsonElement message)
    {
        JsonElement author = message.GetProperty("authorName");

        return new ChatMessage(new ChatRole(message.GetProperty("role").GetString()!), [.. message.GetProperty("contents").EnumerateArray().Select(ReadContent)])
        {
            AuthorName = author.ValueKind == JsonValueKind.String ? author.GetString() : null
        };
    }

    private static AIContent ReadContent(JsonElement content)
    {
        if (content.TryGetProperty("text", out JsonElement text))
        {
            return new TextContent(text.GetString());
        }

        if (content.TryGetProperty("functionCall", out JsonElement name) || content.TryGetProperty("approvalRequest", out name))
        {
            return new FunctionCallContent(
                content.GetProperty("callId").GetString()!,
                name.GetString()!,
                content.GetProperty("arguments").Deserialize<Dictionary<string, object?>>());
        }

        if (content.TryGetProperty("functionResult", out JsonElement callId))
        {
            return new FunctionResultContent(callId.GetString()!, content.GetProperty("result").Clone());
        }

        throw new InvalidOperationException($"Contenuto catturato non gestito: {content.GetRawText()}");
    }

    private static AITool ReadTool(JsonElement tool) =>
        AIFunctionFactory.CreateDeclaration(
            tool.GetProperty("name").GetString()!,
            tool.GetProperty("description").GetString(),
            tool.GetProperty("schema").Clone());
}
