using Dusiburg.AI.O2C.Erp.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Erp.Web.Pages.Stock;

/// <summary>Magazzino (6.5), con il filtro "solo sotto scorta" (G6.6).</summary>
public sealed class IndexModel(ErpApiClient erp) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public bool ShortOnly { get; set; }

    public IReadOnlyList<StockItemView> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Items = await erp.ListStockAsync(ShortOnly, cancellationToken);
}
