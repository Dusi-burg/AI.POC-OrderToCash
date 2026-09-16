using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Orchestrator.Workflow;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Support;

/// <summary>Stato del workflow in memoria: registra avvii e fasi; <see cref="AlreadyReceived"/> simula un evento duplicato.</summary>
internal sealed class RecordingWorkflowStateStore : IWorkflowStateStore
{
    public bool AlreadyReceived { get; init; }

    public int StartCalls { get; private set; }

    public List<(WorkflowPhase Phase, string StateJson)> Updates { get; } = [];

    /// <summary>Ultima fase e ultimi fatti per correlation id: è ciò che la ripresa legge dal database.</summary>
    public Dictionary<string, (WorkflowPhase Phase, string StateJson)> Current { get; } = [];

    public Task<bool> TryStartAsync(string correlationId, string dealId, int dealRevision, bool reprocess, CancellationToken cancellationToken)
    {
        StartCalls++;

        return Task.FromResult(reprocess || !AlreadyReceived);
    }

    public Task UpdateAsync(string correlationId, WorkflowPhase phase, string stateJson, CancellationToken cancellationToken)
    {
        lock (Updates)
        {
            Updates.Add((phase, stateJson));
            Current[correlationId] = (phase, stateJson);
        }

        return Task.CompletedTask;
    }
}
