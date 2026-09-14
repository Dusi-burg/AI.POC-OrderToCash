using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Data.Seed;
using Dusiburg.AI.O2C.Erp.Data;
using Dusiburg.AI.O2C.Erp.Data.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dusiburg.AI.O2C.DbInit;

/// <summary>
/// Crea il database O2C da zero dal modello EF di tutti i DbContext del POC (D30): nessuna migration, nessuno step intermedio.
/// Usato dal tool e dalle fixture dei test, con un database dedicato.
/// </summary>
public static class O2CDatabaseInitializer
{
    public const string LocalConnectionString = @"Server=(localdb)\localdev;Database=O2C;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>Connection string di <c>(localdb)\localdev</c> per un altro database (es. quelli dei test).</summary>
    public static string LocalConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder(LocalConnectionString) { InitialCatalog = database }.ConnectionString;

    /// <summary>
    /// Cancella il database se esiste e lo ricrea con schemi, tabelle, sequence e righe delle tabelle di lookup;
    /// con <paramref name="seed"/> inserisce anche i dati demo (D32).
    /// </summary>
    public static async Task RecreateAsync(string connectionString, bool seed = true, CancellationToken cancellationToken = default)
    {
        await using var erp = CreateErp(connectionString);

        await erp.Database.EnsureDeletedAsync(cancellationToken);

        // Crea il database e le tabelle del primo contesto.
        await erp.Database.EnsureCreatedAsync(cancellationToken);

        // EnsureCreated non fa nulla su un database che ha già tabelle: gli altri contesti creano solo le proprie.
        await using var crm = CreateCrm(connectionString);

        await crm.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);

        if (seed)
        {
            await ErpSeeder.SeedAsync(erp, cancellationToken);
            await CrmSeeder.SeedAsync(crm, DateTimeOffset.UtcNow, cancellationToken);
        }
    }

    /// <summary>Cancella il database se esiste.</summary>
    public static async Task DropAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var erp = CreateErp(connectionString);

        await erp.Database.EnsureDeletedAsync(cancellationToken);
    }

    private static ErpDbContext CreateErp(string connectionString) =>
        new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connectionString).Options);

    private static CrmDbContext CreateCrm(string connectionString) =>
        new(new DbContextOptionsBuilder<CrmDbContext>().UseSqlServer(connectionString).Options);
}
