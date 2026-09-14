using System.Globalization;

namespace Dusiburg.AI.O2C.Shared.Idempotency;

/// <summary>
/// Chiave di idempotenza di <c>create_order</c> (§12): derivata da deal e revisione,
/// calcolata dal codice e mai generata dal modello (D17).
/// </summary>
public static class IdempotencyKey
{
    public const int MaxLength = 100;

    private const string Prefix = "o2c-";

    /// <example><c>From("D-1001", 3)</c> → <c>o2c-D-1001-r3</c></example>
    public static string From(string dealId, int revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dealId);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);

        foreach (var c in dealId)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
            {
                throw new ArgumentException("Il dealId ammette solo lettere, cifre, '-' e '_'.", nameof(dealId));
            }
        }

        // La revisione è solo cifre, quindi l'ultimo "-r" separa sempre in modo univoco dealId e revisione.
        var key = string.Concat(Prefix, dealId, "-r", revision.ToString(CultureInfo.InvariantCulture));

        if (key.Length > MaxLength)
        {
            throw new ArgumentException($"La chiave supera {MaxLength} caratteri.", nameof(dealId));
        }

        return key;
    }
}
