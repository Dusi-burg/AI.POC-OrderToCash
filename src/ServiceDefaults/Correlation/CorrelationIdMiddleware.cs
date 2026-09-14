using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using O2C.Shared.Correlation;
using O2C.Shared.Telemetry;

namespace O2C.ServiceDefaults.Correlation;

/// <summary>
/// Legge <c>x-correlation-id</c> (o ne genera uno se assente o non valido), lo rimanda nella risposta
/// e lo rende disponibile a contesto ambientale, Activity corrente e scope di log.
/// </summary>
public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ICorrelationContext correlationContext,
    ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var inbound = context.Request.Headers[CorrelationId.HeaderName].ToString();
        var correlationId = CorrelationId.IsValid(inbound) ? inbound : CorrelationId.New();

        context.Response.Headers[CorrelationId.HeaderName] = correlationId;

        var activity = Activity.Current;
        activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, correlationId);
        activity?.SetBaggage(O2CTelemetry.Attributes.CorrelationId, correlationId);

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            [O2CTelemetry.Attributes.CorrelationId] = correlationId
        });
        using var correlationScope = correlationContext.Begin(correlationId);

        await next(context);
    }
}
