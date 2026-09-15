using System.Collections.Concurrent;
using Dusiburg.AI.O2C.Shared.Correlation;

namespace Dusiburg.AI.O2C.Mcp.Tests.Support;

/// <summary>
/// Handler primario dell'<c>ErpApiClient</c> nei test (D36): inoltra a <c>Erp.Api</c> in-process oppure, se impostata,
/// usa una risposta simulata; registra il correlation id di ogni richiesta.
/// </summary>
internal sealed class UpstreamHandler(HttpMessageHandler erpApi) : DelegatingHandler(erpApi)
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Simulate { get; set; }

    public ConcurrentQueue<string?> CorrelationIds { get; } = new();

    public void Reset()
    {
        Simulate = null;
        CorrelationIds.Clear();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CorrelationIds.Enqueue(request.Headers.TryGetValues(CorrelationId.HeaderName, out var values) ? values.Single() : null);

        return Simulate is { } simulate ? simulate(request, cancellationToken) : base.SendAsync(request, cancellationToken);
    }

    // L'handler è condiviso da tutta la classe di test, mentre HttpClientFactory ricicla le proprie catene:
    // la chiusura non deve propagarsi all'handler di Erp.Api.
    protected override void Dispose(bool disposing)
    {
    }
}
