namespace Dusiburg.AI.O2C.Shared.Correlation;

/// <summary>
/// Correlation id del flusso di esecuzione corrente.
/// </summary>
public interface ICorrelationContext
{
    string? Current { get; }

    /// <summary>
    /// Imposta l'id per il flusso asincrono corrente; il valore precedente torna attivo al Dispose.
    /// </summary>
    IDisposable Begin(string correlationId);
}
