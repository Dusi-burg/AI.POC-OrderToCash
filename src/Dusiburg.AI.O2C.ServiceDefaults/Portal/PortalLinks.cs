using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dusiburg.AI.O2C.ServiceDefaults.Portal;

/// <summary>
/// Indirizzi delle tre UI della demo (<c>Crm.Web</c>, <c>Erp.Web</c>, <c>Approvals.Web</c>) per i collegamenti fra
/// una e l'altra (G6.8). Li passa l'AppHost dai riferimenti agli endpoint (<c>Links__CrmWeb</c>, …): senza valore il
/// collegamento non si mostra.
/// </summary>
public sealed class PortalLinks
{
    public const string SectionName = "Links";

    public string? CrmWeb { get; set; }

    public string? ErpWeb { get; set; }

    public string? ApprovalsWeb { get; set; }

    public string? CrmDeal(string dealId) => Combine(CrmWeb, $"deals/{Uri.EscapeDataString(dealId)}");

    public string? ErpOrder(string orderNumber) => Combine(ErpWeb, $"orders/{Uri.EscapeDataString(orderNumber)}");

    /// <summary>Tutte le richieste di approvazione del deal, anche già decise.</summary>
    public string? ApprovalsForDeal(string dealId) => Combine(ApprovalsWeb, $"approvals?all=true&dealId={Uri.EscapeDataString(dealId)}");

    private static string? Combine(string? baseUrl, string path) =>
        string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/{path}";
}

public static class PortalLinksExtensions
{
    /// <summary>Lega la sezione <c>Links</c> a <see cref="PortalLinks"/> (da iniettare come <c>IOptions&lt;PortalLinks&gt;</c>).</summary>
    public static IServiceCollection AddPortalLinks(this IServiceCollection services, IConfiguration configuration) =>
        services.Configure<PortalLinks>(configuration.GetSection(PortalLinks.SectionName));
}
