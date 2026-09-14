using Dusiburg.AI.O2C.Crm.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Crm.Data;

/// <summary>
/// CRM mock: schema <c>crm</c> del database O2C (D18, M6). Lo schema si crea da zero dal modello, senza migration (D30).
/// </summary>
public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    public const string Schema = "crm";

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Deal> Deals => Set<Deal>();

    public DbSet<DealLineItem> DealLineItems => Set<DealLineItem>();

    public DbSet<DealNote> DealNotes => Set<DealNote>();

    public DbSet<EnumLookup<DealStage>> DealStages => Set<EnumLookup<DealStage>>();

    public DbSet<EnumLookup<DealStatus>> DealStatuses => Set<EnumLookup<DealStatus>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CrmDbContext).Assembly);
    }
}
