using Dusiburg.AI.O2C.Crm.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Crm.Web.Pages.Companies;
public sealed class DetailsModel(CrmApiClient crm) : PageModel
{
    public CompanyDetailView Company { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string companyId, CancellationToken cancellationToken)
    {
        CompanyDetailView? company = await crm.FindCompanyAsync(companyId, cancellationToken);

        if (company is null)
        {
            return NotFound();
        }

        Company = company;

        return Page();
    }
}
