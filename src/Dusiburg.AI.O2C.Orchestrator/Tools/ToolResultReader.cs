using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Errors;

namespace Dusiburg.AI.O2C.Orchestrator.Tools;

internal readonly record struct ToolResult(bool Succeeded, JsonElement Data, string? ErrorCode, string? ErrorMessage);

/// <summary>
/// Interpreta il valore restituito da un tool: un risultato MCP (<c>structuredContent</c>, <c>content</c>, <c>isError</c>, D33)
/// oppure direttamente l'oggetto del risultato; l'envelope <c>{ error: { code, message } }</c> è sempre un errore.
/// </summary>
internal static class ToolResultReader
{
    public static ToolResult Read(object? value)
    {
        var element = value switch
        {
            JsonElement json => json,
            null => default,
            string text => TryParse(text) ?? JsonSerializer.SerializeToElement(text),
            _ => JsonSerializer.SerializeToElement(value, AgentJson.Options)
        };

        if (element.ValueKind != JsonValueKind.Object)
        {
            return new ToolResult(true, element, null, null);
        }

        if (TryReadError(element, out var code, out var message))
        {
            return new ToolResult(false, element, code, message);
        }

        var isError = element.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;

        if (!isError && element.TryGetProperty("structuredContent", out var structured) && structured.ValueKind == JsonValueKind.Object)
        {
            return new ToolResult(true, structured, null, null);
        }

        if (element.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var text = string.Concat(content.EnumerateArray()
                .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text")
                .Select(block => block.TryGetProperty("text", out var t) ? t.GetString() : null));

            var parsed = TryParse(text);

            if (parsed is { ValueKind: JsonValueKind.Object } payload && TryReadError(payload, out code, out message))
            {
                return new ToolResult(false, payload, code, message);
            }

            return isError
                ? new ToolResult(false, element, ToolErrorCodes.Internal, text)
                : new ToolResult(true, parsed ?? element, null, null);
        }

        return isError
            ? new ToolResult(false, element, ToolErrorCodes.Internal, null)
            : new ToolResult(true, element, null, null);
    }

    private static bool TryReadError(JsonElement element, out string? code, out string? message)
    {
        code = null;
        message = null;

        if (!element.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        code = error.TryGetProperty("code", out var c) ? c.GetString() : ToolErrorCodes.Internal;
        message = error.TryGetProperty("message", out var m) ? m.GetString() : null;

        return true;
    }

    private static JsonElement? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
