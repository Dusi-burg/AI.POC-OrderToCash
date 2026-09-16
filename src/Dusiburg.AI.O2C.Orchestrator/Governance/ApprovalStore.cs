using System.Text.Json;
using Dusiburg.AI.O2C.Orchestration.Data;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Orchestrator.Governance;

/// <summary>Richiesta di approvazione letta dal database, con il payload già deserializzato.</summary>
public sealed record PendingApproval(
    Guid ApprovalId,
    string CorrelationId,
    string DealId,
    int DealRevision,
    ApprovalStatus Status,
    IReadOnlyList<ApprovalReason> Reasons,
    ApprovalPayload Payload,
    string? CheckpointId,
    string? TraceParent,
    string? DecidedBy,
    string? DecisionNote,
    DateTimeOffset RequestedAt);

public interface IApprovalStore
{
    /// <summary>Crea la richiesta pendente con la proposta congelata e il checkpoint da cui riprendere (5.2).</summary>
    Task<Guid> CreatePendingAsync(
        DealRunContext context,
        int dealRevision,
        ApprovalPayload payload,
        IReadOnlyList<ApprovalReason> reasons,
        string? checkpointId,
        string? traceParent,
        CancellationToken cancellationToken);

    Task<PendingApproval?> FindAsync(Guid approvalId, CancellationToken cancellationToken);

    /// <summary>Richieste decise il cui workflow non è ancora stato ripreso: la sweep di riconciliazione le riprende (G5.3).</summary>
    Task<IReadOnlyList<Guid>> ListAwaitingResumeAsync(DateTimeOffset staleResumeBefore, CancellationToken cancellationToken);

    /// <summary>
    /// Prende possesso della ripresa di un workflow: vero solo a chi vince la corsa. Due consegne simultanee della
    /// stessa decisione non possono quindi riprendere lo stesso workflow in parallelo.
    /// </summary>
    Task<bool> TryClaimResumeAsync(string correlationId, DateTimeOffset staleResumeBefore, CancellationToken cancellationToken);

    /// <summary>Richieste pendenti da più del timeout configurato (5.6).</summary>
    Task<IReadOnlyList<PendingApproval>> ListExpiredAsync(DateTimeOffset notRequestedAfter, CancellationToken cancellationToken);

    /// <summary>Porta una richiesta pendente a <see cref="ApprovalStatus.Expired"/>; falso se nel frattempo è stata decisa.</summary>
    Task<bool> TryExpireAsync(Guid approvalId, CancellationToken cancellationToken);

    /// <summary>Fatti del run sospeso, da cui si ricostruisce il contesto alla ripresa.</summary>
    Task<DealRunSnapshot?> GetSnapshotAsync(string correlationId, CancellationToken cancellationToken);

    Task CloseWorkflowAsync(string correlationId, WorkflowPhase phase, string stateJson, CancellationToken cancellationToken);
}

/// <summary>
/// Persistenza delle richieste di approvazione per l'orchestratore (5.2, 5.5, 5.6). Le regole di transizione stanno in
/// <see cref="ApprovalRepository"/>, condivise con <c>Approvals.Web</c>: qui c'è solo l'adattamento allo scope dei servizi.
/// </summary>
public sealed class ApprovalStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : IApprovalStore
{
    public async Task<Guid> CreatePendingAsync(
        DealRunContext context,
        int dealRevision,
        ApprovalPayload payload,
        IReadOnlyList<ApprovalReason> reasons,
        string? checkpointId,
        string? traceParent,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        var request = new ApprovalRequest
        {
            PublicId = Guid.CreateVersion7(),
            CorrelationId = context.CorrelationId,
            DealId = context.DealId,
            DealRevision = dealRevision,
            PayloadJson = JsonSerializer.Serialize(payload, AgentJson.Options),
            ReasonsJson = JsonSerializer.Serialize(reasons, AgentJson.Options),
            Total = payload.Total,
            Status = ApprovalStatus.Pending,
            RequestedAt = timeProvider.GetUtcNow(),
            TraceParent = traceParent,
            CheckpointId = checkpointId
        };

        db.ApprovalRequests.Add(request);

        await db.SaveChangesAsync(cancellationToken);

        return request.PublicId;
    }

    public async Task<PendingApproval?> FindAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = Repository(scope);

        return await repository.FindAsync(approvalId, cancellationToken) is { } request ? Map(request) : null;
    }

    public async Task<IReadOnlyList<Guid>> ListAwaitingResumeAsync(DateTimeOffset staleResumeBefore, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var requests = await Repository(scope).ListAwaitingResumeAsync(staleResumeBefore, cancellationToken);

        return [.. requests.Select(r => r.PublicId)];
    }

    public async Task<bool> TryClaimResumeAsync(string correlationId, DateTimeOffset staleResumeBefore, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        return await Repository(scope).TryClaimResumeAsync(correlationId, staleResumeBefore, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingApproval>> ListExpiredAsync(DateTimeOffset notRequestedAfter, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var requests = await Repository(scope).ListExpiredAsync(notRequestedAfter, cancellationToken);

        return [.. requests.Select(Map)];
    }

    public async Task<bool> TryExpireAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var transition = await Repository(scope).TryDecideAsync(
            approvalId, ApprovalStatus.Expired, decidedBy: null, note: "Scaduta senza decisione.", cancellationToken);

        return transition == ApprovalTransition.Applied;
    }

    public async Task<DealRunSnapshot?> GetSnapshotAsync(string correlationId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        var json = await db.WorkflowStates
            .Where(s => s.CorrelationId == correlationId)
            .Select(s => s.StateJson)
            .SingleOrDefaultAsync(cancellationToken);

        return json is null ? null : JsonSerializer.Deserialize<DealRunSnapshot>(json, AgentJson.Options);
    }

    public async Task CloseWorkflowAsync(string correlationId, WorkflowPhase phase, string stateJson, CancellationToken cancellationToken)
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

    private ApprovalRepository Repository(AsyncServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>(), timeProvider);

    private static PendingApproval Map(ApprovalRequest request) =>
        new(
            request.PublicId,
            request.CorrelationId,
            request.DealId,
            request.DealRevision,
            request.Status,
            JsonSerializer.Deserialize<List<ApprovalReason>>(request.ReasonsJson, AgentJson.Options) ?? [],
            JsonSerializer.Deserialize<ApprovalPayload>(request.PayloadJson, AgentJson.Options)
                ?? throw new InvalidOperationException($"Payload della richiesta {request.PublicId} illeggibile."),
            request.CheckpointId,
            request.TraceParent,
            request.DecidedBy,
            request.DecisionNote,
            request.RequestedAt);
}
