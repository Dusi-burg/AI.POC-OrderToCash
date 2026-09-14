using System.Net;
using Dusiburg.AI.O2C.ServiceDefaults.Correlation;
using Dusiburg.AI.O2C.Shared.Correlation;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.ServiceDefaults;

public class CorrelationIdDelegatingHandlerTests
{
    private readonly AsyncLocalCorrelationContext _context = new();

    [Test]
    public async Task SendAsync_WithAmbientId_AddsHeader()
    {
        var inner = new CapturingHandler();

        using (_context.Begin("corr-ambient"))
        {
            await SendAsync(inner);
        }

        Assert.That(inner.CorrelationIdHeader, Is.EqualTo("corr-ambient"));
    }

    [Test]
    public async Task SendAsync_WithoutAmbientId_DoesNotAddHeader()
    {
        var inner = new CapturingHandler();

        await SendAsync(inner);

        Assert.That(inner.CorrelationIdHeader, Is.Null);
    }

    [Test]
    public async Task SendAsync_WithExplicitHeader_KeepsIt()
    {
        var inner = new CapturingHandler();

        using (_context.Begin("corr-ambient"))
        {
            await SendAsync(inner, request => request.Headers.Add(CorrelationId.HeaderName, "corr-explicit"));
        }

        Assert.That(inner.CorrelationIdHeader, Is.EqualTo("corr-explicit"));
    }

    private async Task SendAsync(CapturingHandler inner, Action<HttpRequestMessage>? configure = null)
    {
        using var invoker = new HttpMessageInvoker(new CorrelationIdDelegatingHandler(_context) { InnerHandler = inner });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://erp-api/api/stock/IND-BRG-001");
        configure?.Invoke(request);

        using var response = await invoker.SendAsync(request, TestContext.CurrentContext.CancellationToken);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? CorrelationIdHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CorrelationIdHeader = request.Headers.TryGetValues(CorrelationId.HeaderName, out var values)
                ? string.Join(",", values)
                : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
