namespace Dusiburg.AI.O2C.Shared.Contracts.Messaging;

/// <summary>Evento del CRM: il deal è passato a ClosedWon (4.5). Il message-id è <c>{dealId}:{revision}</c>.</summary>
public sealed record DealClosedWon(string DealId, int Revision, DateTimeOffset OccurredAt)
{
    public string MessageId => $"{DealId}:{Revision}";
}

/// <summary>Topologia RabbitMQ del trigger (G4.4), dichiarata in modo idempotente da publisher e consumer.</summary>
public static class DealEventsTopology
{
    public const string Exchange = "deal-closed-won";

    public const string RoutingKey = "deal.closed-won";

    public const string Queue = "o2c.orchestrator.deal-closed-won";

    public const string DeadLetterExchange = "o2c.dlx";

    public const string DeadLetterQueue = "o2c.orchestrator.deal-closed-won.dlq";

    /// <summary>Header W3C per collegare la traccia del consumer a quella del publisher.</summary>
    public const string TraceParentHeader = "traceparent";

    /// <summary>Numero di nuovi tentativi già eseguiti su questo messaggio.</summary>
    public const string RetryCountHeader = "x-retry-count";

    /// <summary>Tentativi dopo il primo; oltre si passa alla dead-letter queue.</summary>
    public const int MaxRetries = 3;
}
