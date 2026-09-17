using Dusiburg.AI.O2C.Erp.Web.Api;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Erp.Web.Pages;

/// <summary>Home dell'ERP (6.5): clienti, ordini per stato, prodotti sotto scorta, ultimi ordini.</summary>
public sealed class IndexModel(ErpDashboardService dashboard) : PageModel
{
    public ErpDashboard Dashboard { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Dashboard = await dashboard.LoadAsync(cancellationToken);
}
