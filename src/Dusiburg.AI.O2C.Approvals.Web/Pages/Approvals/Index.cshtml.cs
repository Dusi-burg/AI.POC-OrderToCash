using Dusiburg.AI.O2C.Approvals.Web.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Approvals.Web.Pages.Approvals;

/// <summary>Coda delle approvazioni (5.4): pendenti per default, con il filtro per vederle tutte.</summary>
public sealed class IndexModel(ApprovalDecisionService approvals) : PageModel
{
    /// <summary>Con <c>?all=true</c> l'elenco mostra anche le richieste già decise o scadute.</summary>
    [BindProperty(SupportsGet = true, Name = "all")]
    public bool ShowAll { get; set; }

    public IReadOnlyList<ApprovalView> Requests { get; private set; } = [];

    public string Approver => approvals.Approver;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Requests = await approvals.ListAsync(ShowAll ? null : ApprovalStatus.Pending, cancellationToken);
}
