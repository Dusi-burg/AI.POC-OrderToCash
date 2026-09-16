using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Orchestration.Data;

/// <summary>
/// Stato dell'orchestrazione: schema <c>orch</c> del database O2C (D18, D45). Lo schema si crea da zero dal modello, senza migration (D30).
/// Condiviso fra Orchestrator e Approvals.Web (5.4).
/// </summary>
public sealed class OrchestrationDbContext(DbContextOptions<OrchestrationDbContext> options) : DbContext(options)
{
    public const string Schema = "orch";

    public DbSet<WorkflowState> WorkflowStates => Set<WorkflowState>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<WorkflowCheckpoint> WorkflowCheckpoints => Set<WorkflowCheckpoint>();

    public DbSet<EnumLookup<WorkflowPhase>> WorkflowPhases => Set<EnumLookup<WorkflowPhase>>();

    public DbSet<EnumLookup<ApprovalStatus>> ApprovalStatuses => Set<EnumLookup<ApprovalStatus>>();

    public DbSet<EnumLookup<ApprovalReason>> ApprovalReasons => Set<EnumLookup<ApprovalReason>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrchestrationDbContext).Assembly);
    }
}
