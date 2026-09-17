using Dusiburg.AI.O2C.Approvals.Web.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Approvals.Web.Pages.Approvals;

/// <summary>Coda delle approvazioni (5.4): pendenti per default, con il filtro per vederle tutte e quello per deal (G6.8).</summary>
public sealed class IndexModel(ApprovalDecisionService approvals) : PageModel
{
    /// <summary>Con <c>?all=true</c> l'elenco mostra anche le richieste già decise o scadute.</summary>
    [BindProperty(SupportsGet = true, Name = "all")]
    public bool ShowAll { get; set; }

    /// <summary>Con <c>?dealId=D-1002</c> solo le richieste di quel deal (link dalla pagina del deal in Crm.Web).</summary>
    [BindProperty(SupportsGet = true, Name = "dealId")]
    public string? DealId { get; set; }

    public IReadOnlyList<ApprovalView> Requests { get; private set; } = [];

    public string Approver => approvals.Approver;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Requests = await approvals.ListAsync(
            ShowAll ? null : ApprovalStatus.Pending,
            string.IsNullOrWhiteSpace(DealId) ? null : DealId.Trim(),
            cancellationToken);
}
