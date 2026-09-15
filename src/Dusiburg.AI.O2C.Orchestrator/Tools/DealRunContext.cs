using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tools;

public sealed record ToolCallRecord(string Tool, string Outcome, double DurationMs);

/// <summary>
/// Stato di un run su un deal (3.3): i fatti arrivano dai risultati reali dei tool, non dai riassunti del modello.
/// </summary>
public sealed class DealRunContext(string dealId, string correlationId, string agentName, IReadOnlySet<string> allowedTools)
{
    private readonly Lock _gate = new();
    private readonly List<ToolCallRecord> _calls = [];
    private readonly List<StockCheckDto> _stock = [];

    public string DealId => dealId;

    public string CorrelationId => correlationId;

    public string AgentName => agentName;

    public IReadOnlySet<string> AllowedTools => allowedTools;

    public DealDto? Deal { get; private set; }

    public CompanyDto? Company { get; private set; }

    public int? CustomerId { get; private set; }

    public CreateOrderResponse? Order { get; private set; }

    public DealStatus? CrmStatus { get; private set; }

    public IReadOnlyList<StockCheckDto> Stock
    {
        get
        {
            lock (_gate)
            {
                return [.. _stock];
            }
        }
    }

    public IReadOnlyList<ToolCallRecord> ToolCalls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    internal void RecordCall(ToolCallRecord call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }

    internal void RecordFacts(string qualifiedTool, AIFunctionArguments arguments, JsonElement data)
    {
        try
        {
            lock (_gate)
            {
                switch (qualifiedTool)
                {
                    case AgentToolNames.GetDeal:
                        Deal = data.Deserialize<DealDto>(AgentJson.Options);
                        break;
                    case AgentToolNames.GetCompany:
                        Company = data.Deserialize<CompanyDto>(AgentJson.Options);
                        break;
                    case AgentToolNames.CheckStock when data.Deserialize<StockCheckDto>(AgentJson.Options) is { } stock:
                        _stock.Add(stock);
                        break;
                    case AgentToolNames.GetCustomer when data.Deserialize<GetCustomerResponse>(AgentJson.Options)?.Customer is { } customer:
                        CustomerId = customer.CustomerId;
                        break;
                    case AgentToolNames.CreateCustomer when data.Deserialize<CreateCustomerResponse>(AgentJson.Options) is { } created:
                        CustomerId = created.CustomerId;
                        break;
                    case AgentToolNames.CreateOrder:
                        Order = data.Deserialize<CreateOrderResponse>(AgentJson.Options);
                        break;
                    case AgentToolNames.UpdateDeal when Enum.TryParse<DealStatus>(ReadText(arguments, "status"), out var status):
                        CrmStatus = status;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Un risultato che non rispetta il contratto non diventa un fatto: l'esito lo tratterà come mancante.
        }
    }

    private static string? ReadText(AIFunctionArguments arguments, string name) =>
        arguments.TryGetValue(name, out var value)
            ? value is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : value?.ToString()
            : null;
}
