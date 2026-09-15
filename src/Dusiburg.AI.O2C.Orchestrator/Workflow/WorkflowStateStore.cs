using Dusiburg.AI.O2C.Orchestration.Data;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Orchestrator.Workflow;

public interface IWorkflowStateStore
{
    /// <summary>
    /// Registra l'avvio di un workflow per deal e revisione. Con <paramref name="reprocess"/> falso (evento dal broker) restituisce
    /// <c>false</c> se quel deal alla stessa revisione è già stato ricevuto; con <c>true</c> (CLI) riusa la riga esistente.
    /// </summary>
    Task<bool> TryStartAsync(string correlationId, string dealId, int dealRevision, bool reprocess, CancellationToken cancellationToken);

    Task UpdateAsync(string correlationId, WorkflowPhase phase, string stateJson, CancellationToken cancellationToken);
}

/// <summary>Stato del workflow su <c>orch.WorkflowState</c> (4.4): garanzia finale di idempotenza = indice univoco su deal e revisione.</summary>
public sealed class WorkflowStateStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : IWorkflowStateStore
{
    public async Task<bool> TryStartAsync(string correlationId, string dealId, int dealRevision, bool reprocess, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var now = timeProvider.GetUtcNow();

        var existing = await db.WorkflowStates.SingleOrDefaultAsync(s => s.DealId == dealId && s.DealRevision == dealRevision, cancellationToken);

        if (existing is not null)
        {
            if (!reprocess)
            {
                return false;
            }

            existing.CorrelationId = correlationId;
            existing.Phase = WorkflowPhase.Received;
            existing.StateJson = null;
            existing.UpdatedAt = now;
        }
        else
        {
            db.WorkflowStates.Add(new WorkflowState
            {
                CorrelationId = correlationId,
                DealId = dealId,
                DealRevision = dealRevision,
                Phase = WorkflowPhase.Received,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException exception) when (!reprocess && exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Stesso evento consegnato due volte in parallelo: la riga l'ha inserita l'altra consegna.
            return false;
        }
    }

    public async Task UpdateAsync(string correlationId, WorkflowPhase phase, string stateJson, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        await db.WorkflowStates
            .Where(s => s.CorrelationId == correlationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Phase, phase)
                .SetProperty(x => x.StateJson, stateJson)
                .SetProperty(x => x.UpdatedAt, timeProvider.GetUtcNow()), cancellationToken);
    }
}
