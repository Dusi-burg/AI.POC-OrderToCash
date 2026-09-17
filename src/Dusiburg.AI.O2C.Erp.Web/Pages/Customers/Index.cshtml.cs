using Dusiburg.AI.O2C.Erp.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Erp.Web.Pages.Customers;

public sealed class IndexModel(ErpApiClient erp) : PageModel
{
    public IReadOnlyList<CustomerSummaryView> Customers { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Customers = await erp.ListCustomersAsync(cancellationToken);
}
