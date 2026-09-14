using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Crm.Mcp.Crm;

/// <summary>CRM mock persistente sullo schema <c>crm</c> (D10, M6).</summary>
public sealed class MockCrmClient(CrmDbContext db, TimeProvider timeProvider) : ICrmClient
{
    public const int NoteMaxLength = 1000;

    public const int ErpOrderNumberMaxLength = 20;

    public async Task<DealDto?> GetDealAsync(string dealId, CancellationToken cancellationToken = default)
    {
        var deal = await db.Deals
            .AsNoTracking()
            .Include(d => d.Company)
            .Include(d => d.LineItems)
            .SingleOrDefaultAsync(d => d.Code == dealId, cancellationToken);

        return deal is null
            ? null
            : new DealDto(
                deal.Code,
                deal.Revision,
                deal.Name,
                deal.Amount,
                deal.Currency,
                deal.Stage.ToString(),
                deal.Company.Code,
                deal.LineItems.OrderBy(l => l.Id).Select(l => new DealLineItemDto(l.Sku, l.Quantity, l.UnitPrice)).ToList());
    }

    public async Task<CompanyDto?> GetCompanyAsync(string companyId, CancellationToken cancellationToken = default)
    {
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Code == companyId, cancellationToken);

        return company is null
            ? null
            : new CompanyDto(company.Code, company.Name, company.VatNumber ?? string.Empty, company.Email, company.Address);
    }

    public async Task<UpdateDealResponse?> UpdateDealAsync(UpdateDealRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DealId, nameof(request));

        if (!Enum.IsDefined(request.Status))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Status, "Stato O2C non previsto.");
        }

        if (request.Note is { Length: > NoteMaxLength })
        {
            throw new ArgumentException($"La nota supera {NoteMaxLength} caratteri.", nameof(request));
        }

        if (request.ErpOrderNumber is { Length: > ErpOrderNumberMaxLength })
        {
            throw new ArgumentException($"erpOrderNumber supera {ErpOrderNumberMaxLength} caratteri.", nameof(request));
        }

        var deal = await db.Deals.SingleOrDefaultAsync(d => d.Code == request.DealId, cancellationToken);

        if (deal is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();

        // Aggiornamento scritto da O2C: la revisione non cambia (G1.3), così la chiave di idempotenza resta stabile.
        deal.O2CStatus = request.Status;
        deal.ErpOrderNumber = request.ErpOrderNumber ?? deal.ErpOrderNumber;
        deal.LastNote = request.Note;
        deal.UpdatedAt = now;

        db.DealNotes.Add(new DealNote
        {
            DealId = deal.Id,
            Status = request.Status,
            ErpOrderNumber = request.ErpOrderNumber,
            Note = request.Note,
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateDealResponse(true);
    }
}
