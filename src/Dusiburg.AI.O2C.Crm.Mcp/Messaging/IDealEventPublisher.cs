using Dusiburg.AI.O2C.Shared.Contracts.Messaging;

namespace Dusiburg.AI.O2C.Crm.Mcp.Messaging;

/// <summary>
/// Pubblicazione di <c>deal-closed-won</c>: nel POC RabbitMQ (<see cref="DealEventPublisher"/>), in Fase 7 Service Bus.
/// Dietro interfaccia perché i test della chiusura (Fase 6) non hanno bisogno di un broker.
/// </summary>
public interface IDealEventPublisher
{
    /// <summary>Pubblica l'evento e restituisce il correlation id assegnato al workflow.</summary>
    Task<string> PublishClosedWonAsync(DealClosedWon message, CancellationToken cancellationToken);
}
