using Dusiburg.AI.O2C.Erp.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Erp.Web.Pages.Orders;

/// <summary>Ordini ricevuti (6.5), con i filtri per stato e cliente (G6.6).</summary>
public sealed class IndexModel(ErpApiClient erp) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public OrderStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CustomerId { get; set; }

    public IReadOnlyList<OrderSummaryView> Orders { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Orders = await erp.ListOrdersAsync(Status, CustomerId, cancellationToken);
}
