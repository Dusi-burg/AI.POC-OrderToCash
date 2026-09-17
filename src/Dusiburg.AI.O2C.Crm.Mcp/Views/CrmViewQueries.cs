using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Crm.Mcp.Views;

/// <summary>Filtri dell'elenco dei deal (G6.6); un valore <c>null</c> non filtra.</summary>
public sealed record DealListFilter(DealStage? Stage, DealStatus? O2CStatus, string? CompanyId);

/// <summary>
/// Letture del CRM mock per <c>Crm.Web</c> (6.2): solo query senza tracking, nessuna paginazione ma un limite fisso
/// di righe con ordinamento stabile (G6.6).
/// </summary>
public sealed class CrmViewQueries(CrmDbContext db)
{
    public const int MaxRows = 200;

    public async Task<IReadOnlyList<CompanySummaryView>> ListCompaniesAsync(CancellationToken cancellationToken)
    {
        List<CompanySummaryView> companies = await db.Companies
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Take(MaxRows)
            .Select(c => new CompanySummaryView(c.Code, c.Name, c.VatNumber, c.Email, c.Deals.Count))
            .ToListAsync(cancellationToken);

        return companies;
    }

    public async Task<CompanyDetailView?> FindCompanyAsync(string companyId, CancellationToken cancellationToken)
    {
        Company? company = await db.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == companyId, cancellationToken);

        if (company is null)
        {
            return null;
        }

        IReadOnlyList<DealSummaryView> deals = await ListDealsAsync(new DealListFilter(null, null, companyId), cancellationToken);

        return new CompanyDetailView(company.Code, company.Name, company.VatNumber, company.Email, company.Address, deals);
    }

    public async Task<IReadOnlyList<DealSummaryView>> ListDealsAsync(DealListFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<Deal> query = db.Deals.AsNoTracking();

        if (filter.Stage is { } stage)
        {
            query = query.Where(d => d.Stage == stage);
        }

        if (filter.O2CStatus is { } status)
        {
            query = query.Where(d => d.O2CStatus == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.CompanyId))
        {
            query = query.Where(d => d.Company.Code == filter.CompanyId);
        }

        List<DealSummaryView> deals = await query
            .OrderBy(d => d.Code)
            .Take(MaxRows)
            .Select(d => new DealSummaryView(
                d.Code, d.Name, d.Company.Code, d.Company.Name, d.Amount, d.Currency, d.Stage, d.Revision,
                d.O2CStatus, d.ErpOrderNumber, d.UpdatedAt))
            .ToListAsync(cancellationToken);

        return deals;
    }

    public async Task<DealDetailView?> FindDealAsync(string dealId, CancellationToken cancellationToken)
    {
        Deal? deal = await db.Deals
            .AsNoTracking()
            .Include(d => d.Company)
            .Include(d => d.LineItems)
            .Include(d => d.Notes)
            .AsSplitQuery()
            .SingleOrDefaultAsync(d => d.Code == dealId, cancellationToken);

        if (deal is null)
        {
            return null;
        }

        var summary = new DealSummaryView(
            deal.Code, deal.Name, deal.Company.Code, deal.Company.Name, deal.Amount, deal.Currency, deal.Stage, deal.Revision,
            deal.O2CStatus, deal.ErpOrderNumber, deal.UpdatedAt);

        return new DealDetailView(
            summary,
            deal.LastNote,
            [.. deal.LineItems.OrderBy(l => l.Id).Select(l => new DealLineView(l.Sku, l.Quantity, l.UnitPrice))],
            [.. deal.Notes.OrderBy(n => n.Id).Select(n => new DealNoteView(n.Status, n.ErpOrderNumber, n.Note, n.CreatedAt))]);
    }
}
