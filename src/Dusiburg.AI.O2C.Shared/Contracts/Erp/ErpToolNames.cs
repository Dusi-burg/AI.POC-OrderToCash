namespace Dusiburg.AI.O2C.Shared.Contracts.Erp;

/// <summary>
/// Nomi dei tool di <c>erp-mcp</c> (§6.1), senza prefisso (G2.2): il nome qualificato <c>erp.&lt;tool&gt;</c>
/// lo compone l'orchestratore per policy e telemetria.
/// </summary>
public static class ErpToolNames
{
    public const string GetCustomer = "get_customer";

    public const string CreateCustomer = "create_customer";

    public const string CheckStock = "check_stock";

    public const string CreateOrder = "create_order";

    public const string GetOrder = "get_order";
}
