using Dusiburg.AI.O2C.Approvals.Web.Approvals;
using Dusiburg.AI.O2C.Orchestration.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Approvals.Web.Pages.Approvals;

/// <summary>
/// Dettaglio di una richiesta (5.4): proposta leggibile e azioni Approva/Rifiuta con nota facoltativa. Le azioni
/// passano dallo stesso servizio dell'endpoint di callback, quindi UI e integrazioni si comportano allo stesso modo.
/// </summary>
public sealed class DetailsModel(ApprovalDecisionService approvals, IConfiguration configuration) : PageModel
{
    /// <summary>Base del CRM mock per il collegamento al deal (endpoint dev di Fase 1).</summary>
    public const string CrmBaseSetting = "Approvals:CrmDevBaseUrl";

    public ApprovalView? Approval { get; private set; }

    [BindProperty]
    public string? Note { get; set; }

    public string? Message { get; private set; }

    public string Approver => approvals.Approver;

    public string? DealLink =>
        configuration[CrmBaseSetting] is { Length: > 0 } baseUrl && Approval is { } request
            ? $"{baseUrl.TrimEnd('/')}/dev/deals/{request.Summary.DealId}"
            : null;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Approval = await approvals.FindAsync(id, cancellationToken);

        return Approval is null ? NotFound() : Page();
    }

    public Task<IActionResult> OnPostApproveAsync(Guid id, CancellationToken cancellationToken) =>
        DecideAsync(id, approved: true, cancellationToken);

    public Task<IActionResult> OnPostRejectAsync(Guid id, CancellationToken cancellationToken) =>
        DecideAsync(id, approved: false, cancellationToken);

    private async Task<IActionResult> DecideAsync(Guid id, bool approved, CancellationToken cancellationToken)
    {
        var (transition, _) = await approvals.DecideAsync(id, approved, Note, cancellationToken);

        if (transition == ApprovalTransition.NotFound)
        {
            return NotFound();
        }

        Approval = await approvals.FindAsync(id, cancellationToken);

        Message = transition == ApprovalTransition.Applied
            ? approved ? "Richiesta approvata: il workflow riprenderà e creerà l'ordine." : "Richiesta rifiutata: nessun ordine verrà creato."
            : "La richiesta era già stata decisa: la prima decisione resta valida.";

        return Page();
    }
}
