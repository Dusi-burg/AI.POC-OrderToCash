using Dusiburg.AI.O2C.Crm.Data.Entities;
using Dusiburg.AI.O2C.Shared.Demo;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Crm.Data.Seed;

/// <summary>Dati demo del CRM mock presi da <see cref="DemoCatalog"/> (D5, D32).</summary>
public static class CrmSeeder
{
    /// <summary>Inserisce aziende e deal (stage <c>ContractSent</c>, revisione 1) solo se le tabelle sono vuote.</summary>
    public static async Task SeedAsync(CrmDbContext db, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await db.Companies.AnyAsync(cancellationToken) || await db.Deals.AnyAsync(cancellationToken))
        {
            return;
        }

        var companies = DemoCatalog.Companies.ToDictionary(
            c => c.CompanyId,
            c => new Company { Code = c.CompanyId, Name = c.Name, VatNumber = c.VatNumber, Email = c.Email, Address = c.Address },
            StringComparer.Ordinal);

        db.Companies.AddRange(companies.Values);

        db.Deals.AddRange(DemoCatalog.Deals.Select(d => new Deal
        {
            Code = d.DealId,
            Name = d.Name,
            Amount = d.Amount,
            Currency = d.Currency,
            Stage = DealStage.ContractSent,
            Company = companies[d.CompanyId],
            Revision = 1,
            UpdatedAt = now,
            LineItems = d.Lines.Select(l => new DealLineItem { Sku = l.Sku, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList()
        }));

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Riporta il CRM allo stato del seed per ripetere la demo: cancella note, righe, deal e aziende e reinserisce i dati demo,
    /// in un'unica transazione.
    /// </summary>
    public static async Task ResetAsync(CrmDbContext db, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.DealNotes.ExecuteDeleteAsync(cancellationToken);
            await db.DealLineItems.ExecuteDeleteAsync(cancellationToken);
            await db.Deals.ExecuteDeleteAsync(cancellationToken);
            await db.Companies.ExecuteDeleteAsync(cancellationToken);

            await SeedAsync(db, now, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });
    }
}
