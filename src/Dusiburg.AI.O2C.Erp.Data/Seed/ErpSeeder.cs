using Dusiburg.AI.O2C.Erp.Data.Entities;
using Dusiburg.AI.O2C.Shared.Demo;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Erp.Data.Seed;

/// <summary>Dati demo dell'ERP presi da <see cref="DemoCatalog"/> (D5, D32).</summary>
public static class ErpSeeder
{
    /// <summary>Inserisce prodotti, giacenze e clienti solo se le tabelle sono vuote.</summary>
    public static async Task SeedAsync(ErpDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Products.AnyAsync(cancellationToken) || await db.Customers.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Products.AddRange(DemoCatalog.Products.Select(p => new Product
        {
            Sku = p.Sku,
            Description = p.Description,
            ListPrice = p.ListPrice,
            Uom = p.Uom,
            StockLevel = new StockLevel { OnHand = p.OnHand, Reserved = p.Reserved, LeadTimeDays = p.LeadTimeDays }
        }));

        db.Customers.AddRange(DemoCatalog.Customers.Select(c => new Customer
        {
            Name = c.Name,
            VatNumber = c.VatNumber,
            Email = c.Email,
            Address = c.Address,
            CreditLimit = c.CreditLimit,
            IsBlocked = c.IsBlocked
        }));

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Riporta l'ERP allo stato del seed per ripetere la demo: cancella ordini, clienti, prodotti e giacenze,
    /// riavvia la numerazione degli ordini e reinserisce i dati demo, in un'unica transazione.
    /// </summary>
    public static async Task ResetAsync(ErpDbContext db, CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.OrderLines.ExecuteDeleteAsync(cancellationToken);
            await db.Orders.ExecuteDeleteAsync(cancellationToken);
            await db.StockLevels.ExecuteDeleteAsync(cancellationToken);
            await db.Products.ExecuteDeleteAsync(cancellationToken);
            await db.Customers.ExecuteDeleteAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("ALTER SEQUENCE [erp].[OrderNumberSeq] RESTART WITH 1", cancellationToken);

            await SeedAsync(db, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });
    }
}
