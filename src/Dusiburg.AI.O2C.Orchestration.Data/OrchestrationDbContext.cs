using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Orchestration.Data;

/// <summary>
/// Stato dell'orchestrazione: schema <c>orch</c> del database O2C (D18, D45). Lo schema si crea da zero dal modello, senza migration (D30).
/// </summary>
public sealed class OrchestrationDbContext(DbContextOptions<OrchestrationDbContext> options) : DbContext(options)
{
    public const string Schema = "orch";

    public DbSet<WorkflowState> WorkflowStates => Set<WorkflowState>();

    public DbSet<EnumLookup<WorkflowPhase>> WorkflowPhases => Set<EnumLookup<WorkflowPhase>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrchestrationDbContext).Assembly);
    }
}
