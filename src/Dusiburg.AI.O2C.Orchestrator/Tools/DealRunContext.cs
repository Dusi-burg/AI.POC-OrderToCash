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
public sealed record AgentScope(string AgentName, IReadOnlySet<string> AllowedTools)
{
    /// <summary>Nome dell'ambito con cui l'orchestratore chiama i tool da sé (verifiche e scritture degli esiti).</summary>
    public const string HostName = "Host";

    public bool IsHost => AgentName == HostName;
}

/// <summary>
/// Fatti del run serializzati in <c>WorkflowState.StateJson</c>. È anche il formato da cui il contesto si ricostruisce
/// quando il workflow riprende dopo un'approvazione, in un processo che non ha mai visto quel run (Fase 5).
/// </summary>
public sealed record DealRunSnapshot(
    string DealId,
    string CorrelationId,
    DealDto? Deal,
    CompanyDto? Company,
    IReadOnlyList<StockCheckDto> Stock,
    CustomerDto? Customer,
    bool CustomerCreatedInThisRun,
    CreateOrderResponse? Order,
    DealStatus? CrmStatus,
    AgentVerdict? Verdict,
    IReadOnlyList<HandoffRecord> Handoffs,
    IReadOnlyList<ToolCallRecord> ToolCalls);

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

    public CustomerDto? Customer { get; private set; }

    public int? CustomerId => Customer?.CustomerId;

    /// <summary>Il cliente ERP è stato creato durante questo run: è uno dei motivi di approvazione di §7 (G5.2).</summary>
    public bool CustomerCreatedInThisRun { get; private set; }

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
    public string ToStateJson() => JsonSerializer.Serialize(ToSnapshot(), AgentJson.Options);

    public DealRunSnapshot ToSnapshot()
    {
        lock (_gate)
        {
            return new DealRunSnapshot(
                DealId, CorrelationId, Deal, Company, [.. _stock], Customer, CustomerCreatedInThisRun,
                Order, CrmStatus, Verdict, [.. _handoffs], [.. _calls]);
        }
    }

    /// <summary>
    /// Ricostruisce il contesto di un run sospeso dai fatti persistiti (Fase 5): il processo che riprende il workflow
    /// non ha visto le chiamate ai tool, ma la guardia e l'esito hanno bisogno degli stessi fatti di prima.
    /// </summary>
    public static DealRunContext Restore(DealRunSnapshot snapshot, string agentName, IReadOnlySet<string> allowedTools)
    {
        var context = new DealRunContext(snapshot.DealId, snapshot.CorrelationId, agentName, allowedTools)
        {
            Deal = snapshot.Deal,
            Company = snapshot.Company,
            Customer = snapshot.Customer,
            CustomerCreatedInThisRun = snapshot.CustomerCreatedInThisRun,
            Order = snapshot.Order,
            CrmStatus = snapshot.CrmStatus,
            Verdict = snapshot.Verdict
        };

        context._stock.AddRange(snapshot.Stock);
        context._handoffs.AddRange(snapshot.Handoffs);
        context._calls.AddRange(snapshot.ToolCalls);

        return context;
    }

    /// <summary>
    /// Sostituisce le verifiche di giacenza con quelle fatte dall'host sulle righe proposte: sono le uniche su cui la
    /// policy decide, perché sono le sole di cui si conosce la quantità richiesta. Le chiamate degli agenti restano
    /// visibili in <see cref="ToolCalls"/>.
    /// </summary>
    internal void ReplaceStock(IEnumerable<StockCheckDto> checks)
    {
        lock (_gate)
        {
            _stock.Clear();
            _stock.AddRange(checks);
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
                        Customer = customer;
                        CustomerCreatedInThisRun = false;
                        break;
                    case AgentToolNames.CreateCustomer when data.Deserialize<CreateCustomerResponse>(AgentJson.Options) is { } created:
                        // La risposta porta solo l'id: il resto dell'anagrafica è quello appena inviato all'ERP.
                        Customer = new CustomerDto(
                            created.CustomerId,
                            ReadText(arguments, "name") ?? Company?.Name ?? string.Empty,
                            ReadText(arguments, "vatNumber") ?? Company?.VatNumber ?? string.Empty,
                            ReadText(arguments, "email") ?? Company?.Email ?? string.Empty,
                            CreditLimit: 0m,
                            IsBlocked: false);
                        CustomerCreatedInThisRun = true;
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
