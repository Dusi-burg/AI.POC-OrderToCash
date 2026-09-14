namespace O2C.Shared.Correlation;

public sealed class AsyncLocalCorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<string?> Ambient = new();

    public string? Current => Ambient.Value;

    public IDisposable Begin(string correlationId)
    {
        if (!CorrelationId.IsValid(correlationId))
        {
            throw new ArgumentException("Correlation id non valido.", nameof(correlationId));
        }

        var previous = Ambient.Value;
        Ambient.Value = correlationId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
