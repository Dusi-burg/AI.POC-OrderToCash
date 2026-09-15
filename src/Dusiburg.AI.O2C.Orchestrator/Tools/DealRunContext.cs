using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tools;

public sealed record ToolCallRecord(string Tool, string Outcome, double DurationMs, string? Agent = null);

public sealed record HandoffRecord(string From, string To, string? Reason);

/// <summary>Verdetto di arresto dichiarato da un agente con un tool locale (<c>report_discarded</c>, <c>report_failed</c>).</summary>
public sealed record AgentVerdict(string Agent, DealStatus Status, string Reason);

/// <summary>Agente che usa i tool: nome per telemetria e allow-list dei tool consentiti.</summary>
public sealed record AgentScope(string AgentName, IReadOnlySet<string> AllowedTools);

/// <summary>
/// Stato di un run su un deal (3.3, 4.2): i fatti arrivano dai risultati reali dei tool, non dai riassunti del modello.
/// Nel workflow a tre agenti è il contesto trasferito fra gli agenti e il contenuto di <c>WorkflowState.StateJson</c>.
/// </summary>
public sealed class DealRunContext(string dealId, string correlationId, string agentName, IReadOnlySet<string> allowedTools)
{
    private readonly Lock _gate = new();
    private readonly List<ToolCallRecord> _calls = [];
    private readonly List<StockCheckDto> _stock = [];
    private readonly List<HandoffRecord> _handoffs = [];

    public string DealId => dealId;

    public string CorrelationId => correlationId;

    /// <summary>Ambito di default dei tool (agente singolo); nel workflow ogni agente ha il proprio.</summary>
    public AgentScope DefaultScope { get; } = new(agentName, allowedTools);

    public string AgentName => DefaultScope.AgentName;

    public IReadOnlySet<string> AllowedTools => DefaultScope.AllowedTools;

    public DealDto? Deal { get; private set; }

    public CompanyDto? Company { get; private set; }

    public int? CustomerId { get; private set; }

    public CreateOrderResponse? Order { get; private set; }

    public DealStatus? CrmStatus { get; private set; }

    public AgentVerdict? Verdict { get; private set; }

    /// <summary>Il workflow può terminare: verdetto di arresto registrato, oppure ordine creato e deal aggiornato.</summary>
    public bool IsTerminal => Verdict is not null || (Order is not null && CrmStatus == DealStatus.OrderCreated);

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

    public IReadOnlyList<HandoffRecord> Handoffs
    {
        get
        {
            lock (_gate)
            {
                return [.. _handoffs];
            }
        }
    }

    /// <summary>Fatti del run in JSON, per <c>WorkflowState.StateJson</c> e per i log.</summary>
    public string ToStateJson()
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(new
            {
                DealId,
                CorrelationId,
                Deal,
                Company,
                Stock = _stock,
                CustomerId,
                Order,
                CrmStatus,
                Verdict,
                Handoffs = _handoffs,
                ToolCalls = _calls
            }, AgentJson.Options);
        }
    }

    internal void RecordCall(ToolCallRecord call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }

    internal void RecordHandoff(HandoffRecord handoff)
    {
        lock (_gate)
        {
            _handoffs.Add(handoff);
        }
    }

    internal void RecordVerdict(AgentVerdict verdict)
    {
        lock (_gate)
        {
            // Il primo verdetto vale: un agente non può ribaltare quello di un altro.
            Verdict ??= verdict;
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
