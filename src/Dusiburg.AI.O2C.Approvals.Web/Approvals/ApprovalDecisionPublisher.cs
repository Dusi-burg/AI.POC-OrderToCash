using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using Dusiburg.AI.O2C.Shared.Correlation;
using RabbitMQ.Client;

namespace Dusiburg.AI.O2C.Approvals.Web.Approvals;

/// <summary>
/// Annuncia una decisione all'orchestratore. È dietro un'interfaccia come <c>IDealEventSource</c> nell'orchestratore:
/// in Fase 7 il broker cambia, e i test non hanno bisogno di un broker vero.
/// </summary>
public interface IApprovalDecisionPublisher
{
    Task PublishAsync(ApprovalDecided message, CancellationToken cancellationToken);
}

/// <summary>
/// Pubblicazione su RabbitMQ (5.4). Un errore qui non è fatale: la decisione è già scritta e la sweep di
/// riconciliazione dell'orchestratore riprenderà comunque il workflow (G5.3).
/// </summary>
public sealed class RabbitMqApprovalDecisionPublisher(IConnection connection, ILogger<RabbitMqApprovalDecisionPublisher> logger)
    : IApprovalDecisionPublisher
{
    public async Task PublishAsync(ApprovalDecided message, CancellationToken cancellationToken)
    {
        try
        {
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await channel.ExchangeDeclareAsync(
                ApprovalEventsTopology.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);

            var properties = new BasicProperties
            {
                MessageId = message.MessageId,
                CorrelationId = message.CorrelationId,
                ContentType = "application/json",
                Persistent = true,
                Headers = new Dictionary<string, object?> { [CorrelationId.HeaderName] = message.CorrelationId }
            };

            if (Activity.Current is { } activity)
            {
                properties.Headers[DealEventsTopology.TraceParentHeader] = activity.Id;
            }

            var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);

            await channel.BasicPublishAsync(
                ApprovalEventsTopology.Exchange, ApprovalEventsTopology.RoutingKey, mandatory: false, properties, body, cancellationToken);

            logger.LogInformation("Pubblicato {MessageId} su {Exchange}", message.MessageId, ApprovalEventsTopology.Exchange);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Pubblicazione di {MessageId} fallita: la riconciliazione riprenderà il workflow", message.MessageId);
        }
    }
}
