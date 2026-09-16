using Dusiburg.AI.O2C.Orchestrator.Governance;
using Dusiburg.AI.O2C.Orchestrator.Workflow;
using Dusiburg.AI.O2C.Shared.Correlation;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Support;

/// <summary>
/// Compone il workflow come fa l'host, ma tutto in memoria: modello a copione, tool finti, stato e approvazioni
/// registrati in memoria e checkpoint in memoria. La soglia di approvazione si passa come configurazione.
/// </summary>
internal static class WorkflowTestHost
{
    public static ApprovalPolicy Policy(decimal? thresholdEur = null) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(thresholdEur is null
                ? []
                : new Dictionary<string, string?> { [ApprovalPolicy.ThresholdSetting] = thresholdEur.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            .Build());

    public static DealWorkflowEngine CreateEngine(
        IChatClient chat, FakeO2CTools tools, RecordingWorkflowStateStore states, decimal? thresholdEur = null) =>
        new(new ScriptedModelClientFactory(chat),
            new InMemoryToolCatalog(tools.All()),
            new ApprovalGate(Policy(thresholdEur), NullLogger<ApprovalGate>.Instance),
            states,
            NullLogger<DealWorkflowEngine>.Instance);

    public static DealWorkflowRunner CreateRunner(
        IChatClient chat,
        FakeO2CTools tools,
        RecordingWorkflowStateStore states,
        RecordingApprovalStore approvals,
        decimal? thresholdEur = null) =>
        new(CreateEngine(chat, tools, states, thresholdEur),
            states,
            Checkpoints,
            approvals,
            NullLogger<DealWorkflowRunner>.Instance);

    public static ApprovalResumeRunner CreateResumer(
        IChatClient chat,
        FakeO2CTools tools,
        RecordingWorkflowStateStore states,
        RecordingApprovalStore approvals,
        decimal? thresholdEur = null) =>
        new(CreateEngine(chat, tools, states, thresholdEur),
            approvals,
            Checkpoints,
            new AsyncLocalCorrelationContext(),
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            NullLogger<ApprovalResumeRunner>.Instance);

    public static ApprovalSweepService CreateSweep(RecordingApprovalStore approvals, ApprovalResumeRunner resumer) =>
        new(approvals,
            resumer,
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            NullLogger<ApprovalSweepService>.Instance);

    /// <summary>
    /// Un solo manager per tutti i test: la ripresa avviene su un workflow ricostruito da zero, quindi il checkpoint è
    /// l'unico stato che attraversa i due "processi". Le sessioni sono i correlation id, distinti per test.
    /// In produzione lo store è su SQL (<c>orch.WorkflowCheckpoint</c>).
    /// </summary>
    public static CheckpointManager Checkpoints { get; } = CheckpointManager.Default;
}
