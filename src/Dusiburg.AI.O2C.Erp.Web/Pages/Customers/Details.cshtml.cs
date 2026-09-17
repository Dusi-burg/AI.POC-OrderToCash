using Dusiburg.AI.O2C.Erp.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Erp.Web.Pages.Customers;

public sealed class DetailsModel(ErpApiClient erp) : PageModel
{
    public CustomerDetailView Customer { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int customerId, CancellationToken cancellationToken)
    {
        CustomerDetailView? customer = await erp.FindCustomerAsync(customerId, cancellationToken);

        if (customer is null)
        {
            return NotFound();
        }

        Customer = customer;

        return Page();
    }
}
