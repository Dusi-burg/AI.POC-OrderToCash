using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using O2C.ServiceDefaults.Correlation;
using O2C.Shared.Correlation;
using O2C.Shared.Telemetry;

namespace O2C.Orchestrator.Tests.ServiceDefaults;

public class CorrelationIdMiddlewareTests
{
    private readonly AsyncLocalCorrelationContext _context = new();

    [Fact]
    public async Task InvokeAsync_WithoutHeader_GeneratesId()
    {
        string? seen = null;
        var middleware = CreateMiddleware(() => seen = _context.Current);
        var http = new DefaultHttpContext();

        await middleware.InvokeAsync(http);

        Assert.True(CorrelationId.IsValid(seen));
        Assert.Equal(seen, http.Response.Headers[CorrelationId.HeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WithValidHeader_KeepsInboundId()
    {
        string? seen = null;
        var middleware = CreateMiddleware(() => seen = _context.Current);
        var http = new DefaultHttpContext();
        http.Request.Headers[CorrelationId.HeaderName] = "deal-D-1001-run-42";

        await middleware.InvokeAsync(http);

        Assert.Equal("deal-D-1001-run-42", seen);
        Assert.Equal("deal-D-1001-run-42", http.Response.Headers[CorrelationId.HeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidHeader_GeneratesNewId()
    {
        string? seen = null;
        var middleware = CreateMiddleware(() => seen = _context.Current);
        var http = new DefaultHttpContext();
        http.Request.Headers[CorrelationId.HeaderName] = "bad value\r\ninjected: header";

        await middleware.InvokeAsync(http);

        Assert.True(CorrelationId.IsValid(seen));
        Assert.NotEqual("bad value\r\ninjected: header", seen);
    }

    [Fact]
    public async Task InvokeAsync_TagsCurrentActivity()
    {
        using var activity = new Activity("test-request").Start();
        var middleware = CreateMiddleware(() => { });
        var http = new DefaultHttpContext();
        http.Request.Headers[CorrelationId.HeaderName] = "corr-activity";

        await middleware.InvokeAsync(http);

        Assert.Equal("corr-activity", activity.GetTagItem(O2CTelemetry.Attributes.CorrelationId));
        Assert.Equal("corr-activity", activity.GetBaggageItem(O2CTelemetry.Attributes.CorrelationId));
    }

    [Fact]
    public async Task InvokeAsync_ClearsAmbientIdAfterRequest()
    {
        var middleware = CreateMiddleware(() => { });

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.Null(_context.Current);
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
