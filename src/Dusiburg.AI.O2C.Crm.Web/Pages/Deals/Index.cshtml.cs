using Dusiburg.AI.O2C.Crm.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Crm.Web.Pages.Deals;

/// <summary>Elenco dei deal con i filtri per stage, stato O2C e azienda (G6.6).</summary>
public sealed class IndexModel(CrmApiClient crm) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DealStage? Stage { get; set; }

    [BindProperty(SupportsGet = true)]
    public DealStatus? O2CStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CompanyId { get; set; }

    public IReadOnlyList<DealSummaryView> Deals { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Deals = await crm.ListDealsAsync(Stage, O2CStatus, CompanyId, cancellationToken);
}
