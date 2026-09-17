using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using Dusiburg.AI.O2C.Shared.Correlation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Dusiburg.AI.O2C.Orchestrator.Messaging;

/// <summary>
/// Sorgente degli eventi <c>deal-closed-won</c> (4.5, D46): nel POC RabbitMQ, in Fase 7 Service Bus dietro la stessa interfaccia.
/// </summary>
public interface IDealEventSource;

/// <summary>
/// Consumer RabbitMQ del trigger (4.5): topologia dichiarata in modo idempotente (G4.4), <c>prefetch = 1</c> (G4.5),
/// ack manuale solo dopo l'elaborazione (stato persistito), nuovi tentativi fino a <see cref="DealEventsTopology.MaxRetries"/>,
/// poi dead-letter. Un evento già ricevuto (stesso deal e revisione) viene confermato senza nuovo workflow.
/// </summary>
public sealed class DealClosedWonConsumer(IConnection connection, DealProcessor processor, ILogger<DealClosedWonConsumer> logger)
    : BackgroundService, IDealEventSource
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(DealEventsTopology.Queue, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("In ascolto su {Queue}", DealEventsTopology.Queue);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    internal static async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(DealEventsTopology.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DealEventsTopology.DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(DealEventsTopology.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DealEventsTopology.DeadLetterQueue, DealEventsTopology.DeadLetterExchange, string.Empty, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            DealEventsTopology.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DealEventsTopology.DeadLetterExchange },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DealEventsTopology.Queue, DealEventsTopology.Exchange, DealEventsTopology.RoutingKey, cancellationToken: cancellationToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var headers = delivery.BasicProperties.Headers;
        var retries = ReadInt(headers, DealEventsTopology.RetryCountHeader);

        try
        {
            var message = JsonSerializer.Deserialize<DealClosedWon>(delivery.Body.Span, JsonSerializerOptions.Web)
                ?? throw new JsonException("Messaggio vuoto.");

            var correlationId = ReadText(headers, CorrelationId.HeaderName) is { } inbound && CorrelationId.IsValid(inbound)
                ? inbound
                : CorrelationId.New();

            ActivityContext.TryParse(ReadText(headers, DealEventsTopology.TraceParentHeader), null, out var parentContext);

            var result = await processor.ProcessAsync(message.DealId, correlationId, message.Revision, reprocess: false, parentContext, cancellationToken);

            if (result is null)
            {
                logger.LogInformation("Evento {MessageId} duplicato: confermato senza nuovo workflow", message.MessageId);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (JsonException exception)
        {
            // Messaggio illeggibile: un nuovo tentativo non lo aggiusterebbe.
            logger.LogError(exception, "Messaggio non valido su {Queue}: dead-letter", DealEventsTopology.Queue);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (retries < DealEventsTopology.MaxRetries)
            {
                logger.LogWarning(exception, "Elaborazione di {MessageId} fallita: nuovo tentativo {Retry} di {MaxRetries}",
                    delivery.BasicProperties.MessageId, retries + 1, DealEventsTopology.MaxRetries);

                await RepublishAsync(channel, delivery, retries + 1, cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            }
            else
            {
                logger.LogError(exception, "Elaborazione di {MessageId} fallita dopo {MaxRetries} tentativi: dead-letter",
                    delivery.BasicProperties.MessageId, DealEventsTopology.MaxRetries);

                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken);
            }
        }
    }

    /// <summary>Riaccoda una copia con il contatore aggiornato (gli header di un messaggio non si possono modificare con il nack).</summary>
    private static async Task RepublishAsync(IChannel channel, BasicDeliverEventArgs delivery, int retry, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>())
        {
            [DealEventsTopology.RetryCountHeader] = retry
        };

        var properties = new BasicProperties
        {
            MessageId = delivery.BasicProperties.MessageId,
            CorrelationId = delivery.BasicProperties.CorrelationId,
            ContentType = delivery.BasicProperties.ContentType,
            Persistent = true,
            Headers = headers
        };

        await channel.BasicPublishAsync(delivery.Exchange, delivery.RoutingKey, mandatory: false, properties, delivery.Body, cancellationToken);
    }

    internal static string? ReadText(IDictionary<string, object?>? headers, string name) =>
        headers?.TryGetValue(name, out var value) == true
            ? value switch { byte[] bytes => Encoding.UTF8.GetString(bytes), string text => text, _ => value?.ToString() }
            : null;

    internal static int ReadInt(IDictionary<string, object?>? headers, string name) =>
        headers?.TryGetValue(name, out var value) == true
            ? value switch { int number => number, long number => (int)number, byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed, _ => 0 }
            : 0;
}
