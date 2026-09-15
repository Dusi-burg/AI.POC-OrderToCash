using System.Collections.Frozen;
using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Model;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Agents;

/// <summary>Esito dichiarato dal modello a fine run (output strutturato).</summary>
public sealed record OrderOutcome(string DealId, DealStatus Status, string? ErpOrderNumber, IReadOnlyList<string>? Reasons, string? Note);

/// <summary>Esito del run: stato e numero d'ordine vengono dai fatti dei tool; quello del modello è solo confrontato.</summary>
public sealed record DealProcessingResult(
    string DealId,
    string CorrelationId,
    DealStatus Status,
    string? ErpOrderNumber,
    IReadOnlyList<string> Reasons,
    string? Note,
    bool ModelOutcomeValid,
    string Provider,
    string Model,
    IReadOnlyList<ToolCallRecord> ToolCalls);

/// <summary>
/// Un solo agente che porta un deal fino all'ordine ERP (3.4): nessun handoff, nessuna approvazione.
/// </summary>
public sealed class SingleOrderAgent(IModelClientFactory models, IToolCatalog toolCatalog, ILogger<SingleOrderAgent> logger)
{
    public const string AgentName = "SingleOrderAgent";

    /// <summary>Un tentativo più una richiesta di riformulare l'esito, senza rifare i tool.</summary>
    public const int MaxOutcomeAttempts = 2;

    public static IReadOnlySet<string> AllowedTools { get; } = new[]
    {
        AgentToolNames.GetDeal,
        AgentToolNames.GetCompany,
        AgentToolNames.CheckStock,
        AgentToolNames.GetCustomer,
        AgentToolNames.CreateCustomer,
        AgentToolNames.CreateOrder,
        AgentToolNames.UpdateDeal,
    }.ToFrozenSet();

    public async Task<DealProcessingResult> ProcessAsync(string dealId, string correlationId, CancellationToken cancellationToken)
    {
        var context = new DealRunContext(dealId, correlationId, AgentName, AllowedTools);

        using var model = models.Create();

        var tools = (await toolCatalog.GetToolsAsync(cancellationToken))
            .Where(tool => AllowedTools.Contains(tool.QualifiedName))
            .Select(tool => (AITool)new GuardedToolFunction(tool, context))
            .ToList();

        var chatOptions = model.DefaultOptions.Clone();
        chatOptions.Instructions = SingleOrderPrompt.Instructions;
        chatOptions.Tools = tools;

        var agent = new ChatClientAgent(model.ChatClient, new ChatClientAgentOptions { Name = AgentName, ChatOptions = chatOptions });
        var session = await agent.CreateSessionAsync(cancellationToken);

        // Fase di lavoro senza schema di output: con Ollama lo schema diventa il parametro "format", che vincola la generazione
        // al JSON e il modello non chiama i tool (misurato nello spike S3: esito inventato, zero chiamate).
        await agent.RunAsync(SingleOrderPrompt.Task(dealId), session, cancellationToken: cancellationToken);

        OrderOutcome? outcome = null;
        var message = SingleOrderPrompt.OutcomeRequest;

        for (var attempt = 1; attempt <= MaxOutcomeAttempts && outcome is null; attempt++)
        {
            try
            {
                var response = await agent.RunAsync<OrderOutcome>(
                    message, session, serializerOptions: AgentJson.Options, cancellationToken: cancellationToken);

                outcome = IsValid(response.Result, dealId) ? response.Result : null;
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Esito del modello non valido per {DealId} (tentativo {Attempt})", dealId, attempt);
            }

            message = SingleOrderPrompt.OutcomeRetry;
        }

        return Reconcile(context, outcome, model);
    }

    private static bool IsValid(OrderOutcome? outcome, string dealId) =>
        outcome is not null
        && string.Equals(outcome.DealId, dealId, StringComparison.OrdinalIgnoreCase)
        && outcome.Status is DealStatus.OrderCreated or DealStatus.Failed;

    private static DealProcessingResult Reconcile(DealRunContext context, OrderOutcome? outcome, ModelClient model)
    {
        var reasons = new List<string>(outcome?.Reasons ?? []);
        var orderCreated = context.Order is not null && context.CrmStatus == DealStatus.OrderCreated;
        var status = orderCreated ? DealStatus.OrderCreated : DealStatus.Failed;
        var orderNumber = context.Order?.OrderNumber;

        if (context.Order is null)
        {
            reasons.Add("Nessun ordine creato in ERP.");
        }

        if (context.CrmStatus != DealStatus.OrderCreated)
        {
            reasons.Add("Il deal CRM non è stato aggiornato a OrderCreated.");
        }

        if (outcome is null)
        {
            reasons.Add($"Il modello non ha restituito un esito valido in {MaxOutcomeAttempts} tentativi.");
        }
        else if (outcome.Status != status || !string.Equals(outcome.ErpOrderNumber, orderNumber, StringComparison.Ordinal))
        {
            reasons.Add("L'esito dichiarato dal modello non coincide con i risultati dei tool: prevalgono i tool.");
        }

        return new DealProcessingResult(
            context.DealId, context.CorrelationId, status, orderNumber, reasons, outcome?.Note,
            outcome is not null, model.Provider, model.ModelId, context.ToolCalls);
    }
}
