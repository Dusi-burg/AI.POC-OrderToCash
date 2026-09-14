using System.Diagnostics.CodeAnalysis;

namespace O2C.Shared.Correlation;

/// <summary>
/// Identificativo che lega tutti i passaggi di un workflow (messaggi, chiamate HTTP/MCP, approvazioni, log).
/// Generato una sola volta all'ingresso del workflow.
/// </summary>
public static class CorrelationId
{
    public const string HeaderName = "x-correlation-id";

    public const int MaxLength = 128;

    public static string New() => Guid.CreateVersion7().ToString("D");

    /// <summary>
    /// Un id in ingresso è accettato solo se breve e composto da caratteri sicuri,
    /// così non può iniettare contenuto in log, header o attributi di traccia.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':'))
            {
                return false;
            }
        }

        return true;
    }
}
