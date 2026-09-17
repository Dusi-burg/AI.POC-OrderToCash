using Dusiburg.AI.O2C.Erp.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Erp.Web.Pages.Orders;

/// <summary>Dettaglio dell'ordine per numero, l'unico riferimento che il CRM conosce (G6.8).</summary>
public sealed class DetailsModel(ErpApiClient erp) : PageModel
{
    public OrderDetailView Detail { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string orderNumber, CancellationToken cancellationToken)
    {
        OrderDetailView? detail = await erp.FindOrderAsync(orderNumber, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;

        return Page();
    }
}
