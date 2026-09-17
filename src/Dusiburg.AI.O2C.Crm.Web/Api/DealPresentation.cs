using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;

namespace Dusiburg.AI.O2C.Crm.Web.Api;

/// <summary>Regole di presentazione dei deal: quando si può chiudere, quando la pagina si aggiorna da sola, colori dei badge.</summary>
public static class DealPresentation
{
    /// <summary>Intervallo del refresh automatico della pagina del deal (G6.7).</summary>
    public const int RefreshSeconds = 5;

    /// <summary>I comandi di chiusura valgono solo per un deal aperto (G6.3).</summary>
    public static bool CanClose(DealSummaryView deal) => deal.Stage == DealStage.ContractSent;

    /// <summary>Deal vinto il cui flusso O2C non ha ancora un esito finale: la pagina si aggiorna da sola (G6.7).</summary>
    public static bool IsInFlight(DealSummaryView deal) =>
        deal.Stage == DealStage.ClosedWon && (deal.O2CStatus is null || !deal.O2CStatus.Value.IsTerminal());

    public static string StageLabel(DealStage stage) => stage switch
    {
        DealStage.ContractSent => "Contratto inviato",
        DealStage.ClosedWon => "Chiuso vinto",
        DealStage.ClosedLost => "Chiuso perso",
        _ => stage.ToString()
    };

    public static string StageClass(DealStage stage) => stage switch
    {
        DealStage.ContractSent => "text-bg-info",
        DealStage.ClosedWon => "text-bg-success",
        _ => "text-bg-secondary"
    };

    /// <summary>Stesse tinte di <c>Approvals.Web</c>: grigio in attesa, verde a buon fine, rosso per i fallimenti.</summary>
    public static string StatusClass(DealStatus? status) => status switch
    {
        null => "text-bg-light border",
        DealStatus.ApprovalPending => "text-bg-warning",
        DealStatus.OrderCreated => "text-bg-success",
        DealStatus.Rejected or DealStatus.Failed => "text-bg-danger",
        _ => "text-bg-dark"
    };

    public static string StatusLabel(DealStatus? status) => status?.ToString() ?? "—";
}

/// <summary>Riepilogo della home: deal per stage e per stato O2C, e flussi ancora in corso.</summary>
public sealed record CrmDashboard(
    IReadOnlyDictionary<DealStage, int> ByStage,
    IReadOnlyDictionary<string, int> ByStatus,
    IReadOnlyList<DealSummaryView> InFlight,
    int CompanyCount);

public sealed class CrmDashboardService(CrmApiClient crm)
{
    public async Task<CrmDashboard> LoadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DealSummaryView> deals = await crm.ListDealsAsync(null, null, null, cancellationToken);
        IReadOnlyList<CompanySummaryView> companies = await crm.ListCompaniesAsync(cancellationToken);

        Dictionary<DealStage, int> byStage = Enum.GetValues<DealStage>()
            .ToDictionary(stage => stage, stage => deals.Count(d => d.Stage == stage));

        Dictionary<string, int> byStatus = deals
            .GroupBy(d => DealPresentation.StatusLabel(d.O2CStatus))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());

        return new CrmDashboard(byStage, byStatus, [.. deals.Where(DealPresentation.IsInFlight)], companies.Count);
    }
}
