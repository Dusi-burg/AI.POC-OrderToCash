using Dusiburg.AI.O2C.Shared.Correlation;

namespace Dusiburg.AI.O2C.ServiceDefaults.Correlation;

/// <summary>
/// Propaga il correlation id del contesto ambientale sulle chiamate HTTP in uscita,
/// senza sovrascrivere un header già impostato dal chiamante.
/// </summary>
public sealed class CorrelationIdDelegatingHandler(ICorrelationContext correlationContext) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = correlationContext.Current;

        if (correlationId is not null && !request.Headers.Contains(CorrelationId.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
