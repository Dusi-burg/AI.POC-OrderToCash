using Dusiburg.AI.O2C.Shared.Contracts.Approvals;

namespace Dusiburg.AI.O2C.Shared.Contracts.Messaging;

/// <summary>
/// Decisione presa su una richiesta di approvazione (5.4, G5.3): è un acceleratore della ripresa, non l'unica strada —
/// se il messaggio si perde, la sweep di riconciliazione dell'orchestratore trova comunque la richiesta decisa.
/// </summary>
public sealed record ApprovalDecided(Guid ApprovalId, string CorrelationId, string DealId, ApprovalStatus Decision, DateTimeOffset DecidedAt)
{
    public string MessageId => $"{ApprovalId:N}:{Decision}";
}

/// <summary>Topologia RabbitMQ della decisione (5.4), dichiarata in modo idempotente da publisher e consumer.</summary>
public static class ApprovalEventsTopology
{
    public const string Exchange = "approval-decided";

    public const string RoutingKey = "approval.decided";

    public const string Queue = "o2c.orchestrator.approval-decided";

    /// <summary>Le decisioni condividono la dead-letter della topologia dei deal.</summary>
    public const string DeadLetterExchange = DealEventsTopology.DeadLetterExchange;

    public const string DeadLetterQueue = "o2c.orchestrator.approval-decided.dlq";

    public const int MaxRetries = DealEventsTopology.MaxRetries;
}
