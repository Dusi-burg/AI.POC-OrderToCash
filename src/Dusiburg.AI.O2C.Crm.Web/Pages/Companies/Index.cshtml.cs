using Dusiburg.AI.O2C.Crm.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Crm.Web.Pages.Companies;

public sealed class IndexModel(CrmApiClient crm) : PageModel
{
    public IReadOnlyList<CompanySummaryView> Companies { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Companies = await crm.ListCompaniesAsync(cancellationToken);
}
