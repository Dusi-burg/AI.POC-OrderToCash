using Dusiburg.AI.O2C.Erp.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Erp.Data;

/// <summary>
/// ERP mock: schema <c>erp</c> del database O2C (D18). Lo schema si crea da zero dal modello, senza migration (D30).
/// </summary>
public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public const string Schema = "erp";

    public const string OrderNumberSequence = "OrderNumberSeq";

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockLevel> StockLevels => Set<StockLevel>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<EnumLookup<OrderStatus>> OrderStatuses => Set<EnumLookup<OrderStatus>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Numeratore di OrderNumber (G1.2): il formato SO-yyyy-000000 lo compone il codice.
        modelBuilder.HasSequence<long>(OrderNumberSequence).StartsAt(1).IncrementsBy(1);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpDbContext).Assembly);
    }
}
