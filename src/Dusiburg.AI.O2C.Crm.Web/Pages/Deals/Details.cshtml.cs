using Dusiburg.AI.O2C.Crm.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.O2C.Crm.Web.Pages.Deals;

/// <summary>
/// Dettaglio del deal (6.4) con i comandi Chiudi vinto / Chiudi perso. Dopo il comando si torna in GET (post-redirect-get)
/// con l'esito in TempData, così il refresh automatico non ripete la POST.
/// </summary>
public sealed class DetailsModel(CrmApiClient crm) : PageModel
{
    public DealDetailView Detail { get; private set; } = null!;

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? MessageKind { get; set; }

    public bool CanClose => DealPresentation.CanClose(Detail.Deal);

    public bool IsInFlight => DealPresentation.IsInFlight(Detail.Deal);

    public async Task<IActionResult> OnGetAsync(string dealId, CancellationToken cancellationToken)
    {
        DealDetailView? detail = await crm.FindDealAsync(dealId, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;

        return Page();
    }

    public Task<IActionResult> OnPostWonAsync(string dealId, CancellationToken cancellationToken) =>
        CloseAsync(dealId, DealCloseOutcome.Won, cancellationToken);

    public Task<IActionResult> OnPostLostAsync(string dealId, CancellationToken cancellationToken) =>
        CloseAsync(dealId, DealCloseOutcome.Lost, cancellationToken);

    private async Task<IActionResult> CloseAsync(string dealId, DealCloseOutcome outcome, CancellationToken cancellationToken)
    {
        CloseDealResult result = await crm.CloseDealAsync(dealId, outcome, cancellationToken);

        if (result.Status == CloseDealStatus.NotFound)
        {
            return NotFound();
        }

        Message = result.Message;
        MessageKind = result.Status switch
        {
            CloseDealStatus.Closed => "success",
            CloseDealStatus.AlreadyClosed => "warning",
            _ => "danger"
        };

        return RedirectToPage(new { dealId });
    }
}
