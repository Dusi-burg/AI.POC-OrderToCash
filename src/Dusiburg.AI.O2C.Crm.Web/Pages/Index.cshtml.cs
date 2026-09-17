using Dusiburg.AI.O2C.Crm.Web.Api;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Crm.Web.Pages;

/// <summary>Home del CRM (6.4): deal per stage e per stato O2C, flussi in corso.</summary>
public sealed class IndexModel(CrmDashboardService dashboard) : PageModel
{
    public CrmDashboard Dashboard { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Dashboard = await dashboard.LoadAsync(cancellationToken);
}
