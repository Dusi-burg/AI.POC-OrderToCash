using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Erp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dusiburg.AI.O2C.DbInit;

/// <summary>
/// Crea il database O2C da zero dal modello EF di tutti i DbContext del POC (D30): nessuna migration, nessuno step intermedio.
/// Riusabile dalle fixture dei test con un database dedicato.
/// </summary>
public static class O2CDatabaseInitializer
{
    public const string LocalConnectionString = @"Server=(localdb)\localdev;Database=O2C;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>Cancella il database se esiste e lo ricrea con schemi, tabelle, sequence e righe delle tabelle di lookup.</summary>
    public static async Task RecreateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var erp = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connectionString).Options);

        await erp.Database.EnsureDeletedAsync(cancellationToken);

        // Crea il database e le tabelle del primo contesto.
        await erp.Database.EnsureCreatedAsync(cancellationToken);

        // EnsureCreated non fa nulla su un database che ha già tabelle: gli altri contesti creano solo le proprie.
        await using var crm = new CrmDbContext(new DbContextOptionsBuilder<CrmDbContext>().UseSqlServer(connectionString).Options);

        await crm.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
    }
}
