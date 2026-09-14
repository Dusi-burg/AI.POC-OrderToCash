using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Dusiburg.AI.O2C.ServiceDefaults.Correlation;
using Dusiburg.AI.O2C.Shared.Correlation;
using Dusiburg.AI.O2C.Shared.Telemetry;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.ServiceDefaults;

public class CorrelationIdMiddlewareTests
{
    private readonly AsyncLocalCorrelationContext _context = new();

    [Test]
    public async Task InvokeAsync_WithoutHeader_GeneratesId()
    {
        string? seen = null;
        var middleware = CreateMiddleware(() => seen = _context.Current);
        var http = new DefaultHttpContext();

        await middleware.InvokeAsync(http);

        Assert.That(CorrelationId.IsValid(seen), Is.True);
        Assert.That(http.Response.Headers[CorrelationId.HeaderName].ToString(), Is.EqualTo(seen));
    }

    [Test]
    public async Task InvokeAsync_WithValidHeader_KeepsInboundId()
    {
        string? seen = null;
        var middleware = CreateMiddleware(() => seen = _context.Current);
        var http = new DefaultHttpContext();
        http.Request.Headers[CorrelationId.HeaderName] = "deal-D-1001-run-42";

        await middleware.InvokeAsync(http);

        Assert.That(seen, Is.EqualTo("deal-D-1001-run-42"));
        Assert.That(http.Response.Headers[CorrelationId.HeaderName].ToString(), Is.EqualTo("deal-D-1001-run-42"));
    }

    [Test]
    public async Task InvokeAsync_WithInvalidHeader_GeneratesNewId()
    {
        string? seen = null;
        var middleware = CreateMiddleware(() => seen = _context.Current);
        var http = new DefaultHttpContext();
        http.Request.Headers[CorrelationId.HeaderName] = "bad value\r\ninjected: header";

        await middleware.InvokeAsync(http);

        Assert.That(CorrelationId.IsValid(seen), Is.True);
        Assert.That(seen, Is.Not.EqualTo("bad value\r\ninjected: header"));
    }

    [Test]
    public async Task InvokeAsync_TagsCurrentActivity()
    {
        using var activity = new Activity("test-request").Start();
        var middleware = CreateMiddleware(() => { });
        var http = new DefaultHttpContext();
        http.Request.Headers[CorrelationId.HeaderName] = "corr-activity";

        await middleware.InvokeAsync(http);

        Assert.That(activity.GetTagItem(O2CTelemetry.Attributes.CorrelationId), Is.EqualTo("corr-activity"));
        Assert.That(activity.GetBaggageItem(O2CTelemetry.Attributes.CorrelationId), Is.EqualTo("corr-activity"));
    }

    [Test]
    public async Task InvokeAsync_ClearsAmbientIdAfterRequest()
    {
        var middleware = CreateMiddleware(() => { });

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.That(_context.Current, Is.Null);
    }

    private CorrelationIdMiddleware CreateMiddleware(Action onNext) =>
        new(_ =>
            {
                onNext();
                return Task.CompletedTask;
            },
            _context,
            NullLogger<CorrelationIdMiddleware>.Instance);
}
