using System.Text.Json;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Orchestrator.Governance;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Support;

/// <summary>
/// Richieste di approvazione in memoria: riproduce le transizioni ammesse solo da <c>Pending</c> e la fase del workflow,
/// così i test di sospensione, ripresa, doppia consegna e scadenza non hanno bisogno del database.
/// </summary>
internal sealed class RecordingApprovalStore(RecordingWorkflowStateStore? workflowStates = null) : IApprovalStore
{
    private readonly Dictionary<Guid, PendingApproval> _requests = [];
    private readonly Dictionary<string, WorkflowPhase> _phases = [];
    private readonly Dictionary<string, DealRunSnapshot> _snapshots = [];

    public List<PendingApproval> Created { get; } = [];

    public int ResumedWorkflows { get; private set; }

    /// <summary>Quante volte il possesso della ripresa è stato preso: con due consegne concorrenti deve restare 1.</summary>
    public int Claims { get; private set; }

    public Task<Guid> CreatePendingAsync(
        DealRunContext context,
        int dealRevision,
        ApprovalPayload payload,
        IReadOnlyList<ApprovalReason> reasons,
        string? checkpointId,
        string? traceParent,
        CancellationToken cancellationToken)
    {
        var approval = new PendingApproval(
            Guid.CreateVersion7(), context.CorrelationId, context.DealId, dealRevision, ApprovalStatus.Pending,
            reasons, payload, checkpointId, traceParent, null, null, DateTimeOffset.UtcNow);

        _requests[approval.ApprovalId] = approval;
        Created.Add(approval);

        return Task.FromResult(approval.ApprovalId);
    }

    /// <summary>Decisione presa dall'approvatore, come farebbe <c>Approvals.Web</c>.</summary>
    public void Decide(Guid approvalId, ApprovalStatus decision, string? decidedBy = "approver@test", string? note = null) =>
        _requests[approvalId] = _requests[approvalId] with { Status = decision, DecidedBy = decidedBy, DecisionNote = note };

    public void SetPhase(string correlationId, WorkflowPhase phase) => _phases[correlationId] = phase;

    public void SetSnapshot(string correlationId, DealRunSnapshot snapshot) => _snapshots[correlationId] = snapshot;

    /// <summary>Fase corrente: quella impostata dai test, altrimenti l'ultima scritta dal workflow sullo stato condiviso.</summary>
    public WorkflowPhase? PhaseOf(string correlationId) =>
        _phases.TryGetValue(correlationId, out var phase) ? phase
        : workflowStates?.Current.TryGetValue(correlationId, out var current) == true ? current.Phase
        : null;

    public Task<PendingApproval?> FindAsync(Guid approvalId, CancellationToken cancellationToken) =>
        Task.FromResult(_requests.TryGetValue(approvalId, out var approval) ? approval : null);

    public Task<IReadOnlyList<Guid>> ListAwaitingResumeAsync(DateTimeOffset staleResumeBefore, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(
            [.. _requests.Values.Where(r => r.Status != ApprovalStatus.Pending && PhaseOf(r.CorrelationId) == WorkflowPhase.AwaitingApproval).Select(r => r.ApprovalId)]);

    /// <summary>
    /// Possesso atomico come l'UPDATE condizionale del database: sotto lock, e solo a chi trova il workflow in attesa.
    /// Senza questa semantica il test delle riprese concorrenti non proverebbe nulla.
    /// </summary>
    public Task<bool> TryClaimResumeAsync(string correlationId, DateTimeOffset staleResumeBefore, CancellationToken cancellationToken)
    {
        lock (_phases)
        {
            if (PhaseOf(correlationId) != WorkflowPhase.AwaitingApproval)
            {
                return Task.FromResult(false);
            }

            _phases[correlationId] = WorkflowPhase.Resuming;
            Claims++;

            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<PendingApproval>> ListExpiredAsync(DateTimeOffset notRequestedAfter, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PendingApproval>>(
            [.. _requests.Values.Where(r => r.Status == ApprovalStatus.Pending && r.RequestedAt <= notRequestedAfter)]);

    public Task<bool> TryExpireAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        if (!_requests.TryGetValue(approvalId, out var approval) || approval.Status != ApprovalStatus.Pending)
        {
            return Task.FromResult(false);
        }

        _requests[approvalId] = approval with { Status = ApprovalStatus.Expired };

        return Task.FromResult(true);
    }

    /// <summary>Fatti del run: quelli impostati dai test, altrimenti quelli scritti dal workflow in <c>StateJson</c>.</summary>
    public Task<DealRunSnapshot?> GetSnapshotAsync(string correlationId, CancellationToken cancellationToken)
    {
        if (_snapshots.TryGetValue(correlationId, out var snapshot))
        {
            return Task.FromResult<DealRunSnapshot?>(snapshot);
        }

        var json = workflowStates?.Current.TryGetValue(correlationId, out var current) == true ? current.StateJson : null;

        return Task.FromResult(json is null ? null : JsonSerializer.Deserialize<DealRunSnapshot>(json, JsonSerializerOptions.Web));
    }

    public Task CloseWorkflowAsync(string correlationId, WorkflowPhase phase, string stateJson, CancellationToken cancellationToken)
    {
        ResumedWorkflows++;
        _phases[correlationId] = phase;

        return Task.CompletedTask;
    }
}
