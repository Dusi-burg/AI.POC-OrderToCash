using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using Dusiburg.AI.O2C.Shared.Correlation;
using RabbitMQ.Client;

namespace Dusiburg.AI.O2C.Crm.Mcp.Messaging;

/// <summary>
/// Pubblica <c>deal-closed-won</c> (4.5, G4.3): simula il webhook del CRM verso l'ingestion. Il correlation id del workflow nasce qui.
/// </summary>
public sealed class DealEventPublisher(IConnection connection, ICorrelationContext correlationContext, ILogger<DealEventPublisher> logger)
    : IDealEventPublisher
{
    /// <inheritdoc />
    public async Task<string> PublishClosedWonAsync(DealClosedWon message, CancellationToken cancellationToken)
    {
        var correlationId = correlationContext.Current ?? CorrelationId.New();

        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(DealEventsTopology.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);

        var properties = new BasicProperties
        {
            MessageId = message.MessageId,
            CorrelationId = correlationId,
            ContentType = "application/json",
            Persistent = true,
            Headers = new Dictionary<string, object?> { [CorrelationId.HeaderName] = correlationId }
        };

        if (Activity.Current is { } activity)
        {
            properties.Headers[DealEventsTopology.TraceParentHeader] = activity.Id;
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);

        await channel.BasicPublishAsync(DealEventsTopology.Exchange, DealEventsTopology.RoutingKey, mandatory: false, properties, body, cancellationToken);

        logger.LogInformation("Pubblicato {MessageId} su {Exchange} con correlation id {CorrelationId}", message.MessageId, DealEventsTopology.Exchange, correlationId);

        return correlationId;
    }
}
