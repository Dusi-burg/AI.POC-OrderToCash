using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Workflow;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Dusiburg.AI.O2C.Orchestrator.Messaging;

/// <summary>
/// Consumer delle decisioni di approvazione (5.5, G5.3). È solo un acceleratore: la ripresa vera è idempotente e la
/// sweep di riconciliazione trova comunque le richieste decise, quindi un messaggio perso non blocca nulla e un
/// messaggio consegnato due volte non crea un secondo ordine.
/// </summary>
public sealed class ApprovalDecidedConsumer(IConnection connection, ApprovalResumeRunner resumer, ILogger<ApprovalDecidedConsumer> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(ApprovalEventsTopology.Queue, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("In ascolto su {Queue}", ApprovalEventsTopology.Queue);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    internal static async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(ApprovalEventsTopology.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(ApprovalEventsTopology.DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(ApprovalEventsTopology.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ApprovalEventsTopology.DeadLetterQueue, ApprovalEventsTopology.DeadLetterExchange, string.Empty, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            ApprovalEventsTopology.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = ApprovalEventsTopology.DeadLetterExchange },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ApprovalEventsTopology.Queue, ApprovalEventsTopology.Exchange, ApprovalEventsTopology.RoutingKey, cancellationToken: cancellationToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<ApprovalDecided>(delivery.Body.Span, JsonSerializerOptions.Web)
                ?? throw new JsonException("Messaggio vuoto.");

            var outcome = await resumer.ResumeAsync(message.ApprovalId, cancellationToken);

            logger.LogInformation(
                "Decisione {ApprovalDecision} su {ApprovalId} ({DealId}): {ResumeOutcome}",
                message.Decision, message.ApprovalId, message.DealId, outcome);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Messaggio non valido su {Queue}: dead-letter", ApprovalEventsTopology.Queue);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Nessun nuovo tentativo qui: la sweep di riconciliazione riprova con la stessa idempotenza.
            logger.LogError(exception, "Ripresa fallita per {MessageId}: se ne occuperà la riconciliazione", delivery.BasicProperties.MessageId);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
    }
}
