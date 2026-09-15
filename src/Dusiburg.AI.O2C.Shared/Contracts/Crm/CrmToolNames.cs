namespace Dusiburg.AI.O2C.Shared.Contracts.Crm;

/// <summary>
/// Nomi dei tool di <c>crm-mcp</c> (§6.2), senza prefisso (G2.2): il nome qualificato <c>crm.&lt;tool&gt;</c>
/// lo compone l'orchestratore per policy e telemetria.
/// </summary>
public static class CrmToolNames
{
    public const string GetDeal = "get_deal";

    public const string GetCompany = "get_company";

    public const string UpdateDeal = "update_deal";
}
