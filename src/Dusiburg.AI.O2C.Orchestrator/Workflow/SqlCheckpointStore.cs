using System.Text.Json;
using Dusiburg.AI.O2C.Orchestration.Data;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Orchestrator.Workflow;

/// <summary>
/// Store dei checkpoint di Agent Framework su <c>orch.WorkflowCheckpoint</c> (D16, meccanismo A): la sessione è il
/// correlation id del workflow, così un processo diverso può riprendere il run dopo l'approvazione.
/// L'ordine di commit richiesto dal contratto dello store è quello della PK crescente.
/// </summary>
public sealed class SqlCheckpointStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : ICheckpointStore<JsonElement>
{
    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(string sessionId, JsonElement value, CheckpointInfo? parent = null)
    {
        var checkpointId = Guid.CreateVersion7().ToString("N");

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        db.WorkflowCheckpoints.Add(new WorkflowCheckpoint
        {
            SessionId = sessionId,
            CheckpointId = checkpointId,
            ParentCheckpointId = parent?.CheckpointId,
            PayloadJson = value.GetRawText(),
            CreatedAt = timeProvider.GetUtcNow()
        });

        await db.SaveChangesAsync();

        return new CheckpointInfo(sessionId, checkpointId);
    }

    public async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        var payload = await db.WorkflowCheckpoints
            .Where(c => c.SessionId == sessionId && c.CheckpointId == key.CheckpointId)
            .Select(c => c.PayloadJson)
            .SingleOrDefaultAsync()
            ?? throw new InvalidOperationException($"Checkpoint '{key.CheckpointId}' non trovato per la sessione '{sessionId}'.");

        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    public async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(string sessionId, CheckpointInfo? withParent = null)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        var query = db.WorkflowCheckpoints.Where(c => c.SessionId == sessionId);

        if (withParent is not null)
        {
            query = query.Where(c => c.ParentCheckpointId == withParent.CheckpointId);
        }

        var ids = await query.OrderBy(c => c.Id).Select(c => c.CheckpointId).ToListAsync();

        return ids.Select(id => new CheckpointInfo(sessionId, id));
    }
}
