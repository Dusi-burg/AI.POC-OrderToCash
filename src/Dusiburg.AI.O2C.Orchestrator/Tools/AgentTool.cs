using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tools;

/// <summary>
/// Tool disponibile agli agenti: la funzione espone al modello il nome del server (R5, senza punto),
/// <see cref="QualifiedName"/> (<c>erp.create_order</c>) serve ad allow-list, policy e telemetria.
/// </summary>
public sealed record AgentTool(string QualifiedName, AIFunction Function, bool Sensitive);

public interface IToolCatalog
{
    Task<IReadOnlyList<AgentTool>> GetToolsAsync(CancellationToken cancellationToken);
}

public static class AgentToolNames
{
    public const string Erp = "erp";

    public const string Crm = "crm";

    public const string GetDeal = Crm + "." + CrmToolNames.GetDeal;

    public const string GetCompany = Crm + "." + CrmToolNames.GetCompany;

    public const string UpdateDeal = Crm + "." + CrmToolNames.UpdateDeal;

    public const string GetCustomer = Erp + "." + ErpToolNames.GetCustomer;

    public const string CreateCustomer = Erp + "." + ErpToolNames.CreateCustomer;

    public const string CheckStock = Erp + "." + ErpToolNames.CheckStock;

    public const string CreateOrder = Erp + "." + ErpToolNames.CreateOrder;

    public const string GetOrder = Erp + "." + ErpToolNames.GetOrder;
}

internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
