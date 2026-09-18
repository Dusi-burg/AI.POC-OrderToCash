using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Demo;

namespace Dusiburg.AI.O2C.Orchestrator.Governance;

/// <summary>
/// Dati su cui la policy decide: righe **proposte** dall'agente, verifiche di giacenza raccolte dal workflow e cliente
/// risolto in ERP. Il totale non arriva dal modello: lo calcola la policy dalle righe (§12, D17).
/// </summary>
public sealed record ApprovalContext(
    IReadOnlyList<OrderLineInput> Lines,
    IReadOnlyList<StockCheckDto> Stock,
    CustomerDto? Customer,
    bool CustomerCreatedInThisRun)
{
    public decimal Total => Lines.Sum(line => line.Quantity * line.UnitPrice);
}

/// <summary>Esito della policy: se serve l'approvazione e per quali regole di §7.</summary>
public sealed record ApprovalDecision(bool Required, IReadOnlyList<ApprovalReason> Reasons)
{
    public static ApprovalDecision NotRequired { get; } = new(false, []);
}

/// <summary>
/// Una regola di approvazione, con il testo che la spiega a chi non sviluppa (D64).
/// <para>
/// <see cref="Title"/> e <see cref="When"/> non sono commenti: sono la regola raccontata, e da essi nasce
/// <c>docs/regole-di-approvazione.md</c>. Il documento non può quindi divergere da ciò che il sistema fa davvero,
/// perché è generato dalla stessa lista che decide, e un test fallisce se i due si scostano.
/// </para>
/// </summary>
/// <param name="Reason">Motivo registrato sulla richiesta di approvazione.</param>
/// <param name="Title">Nome della regola, in italiano.</param>
/// <param name="When">Quando scatta, in una frase leggibile da chiunque.</param>
/// <param name="Applies">La condizione vera e propria, valutata sul contesto e sulla soglia in vigore.</param>
public sealed record ApprovalRule(
    ApprovalReason Reason,
    string Title,
    string When,
    Func<ApprovalContext, decimal, bool> Applies);

/// <summary>
/// Regole di approvazione di §7 (5.1): deterministiche, in C#, **unica** fonte di verità — i prompt degli agenti non
/// contengono soglie né condizioni. La soglia si legge da <c>APPROVAL_THRESHOLD_EUR</c> a ogni valutazione.
/// <para>
/// Le regole restano compilate di proposito (D64): questo è il punto che decide se servono una firma e dei soldi, e
/// cambiarne una deve costare una modifica al codice, una build e un test, non la riga di un file di configurazione.
/// </para>
/// </summary>
public sealed class ApprovalPolicy(IConfiguration configuration)
{
    public const string ThresholdSetting = "APPROVAL_THRESHOLD_EUR";

    /// <summary>Soglia di default di §7, la stessa attorno a cui sono costruiti gli scenari demo.</summary>
    public const decimal DefaultThresholdEur = DemoCatalog.ApprovalThresholdEur;

    /// <summary>
    /// Le quattro regole, nell'ordine in cui compaiono fra i motivi di una richiesta. Aggiungerne una significa
    /// aggiungere una riga qui: la valutazione, il documento e la telemetria la prendono da sola.
    /// </summary>
    public static IReadOnlyList<ApprovalRule> Rules { get; } =
    [
        new(ApprovalReason.OverThreshold,
            "Importo sopra la soglia",
            "il totale dell'ordine supera la soglia, di base 10.000 €. Un ordine esattamente pari alla soglia passa senza approvazione",
            (context, threshold) => context.Total > threshold),

        new(ApprovalReason.InsufficientStock,
            "Merce insufficiente",
            "per almeno una riga la merce disponibile non basta. L'ordine non viene ridotto: si crea per intero e va in arretrato",
            (context, _) => context.Lines.Any(line => IsInsufficient(context.Stock, line))),

        new(ApprovalReason.NewCustomer,
            "Cliente nuovo",
            "il cliente non era presente nel gestionale ed è stato creato durante questa lavorazione",
            (context, _) => context.CustomerCreatedInThisRun),

        new(ApprovalReason.BlockedCustomer,
            "Cliente bloccato",
            "il gestionale segna il cliente come bloccato. Qui l'approvazione è l'unica strada possibile",
            (context, _) => context.Customer is { IsBlocked: true })
    ];

    public decimal ThresholdEur =>
        decimal.TryParse(configuration[ThresholdSetting], System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : DefaultThresholdEur;

    public ApprovalDecision Evaluate(ApprovalContext context)
    {
        var threshold = ThresholdEur;

        List<ApprovalReason> reasons =
        [
            .. Rules.Where(rule => rule.Applies(context, threshold)).Select(rule => rule.Reason)
        ];

        return reasons.Count == 0 ? ApprovalDecision.NotRequired : new ApprovalDecision(true, reasons);
    }

    /// <summary>
    /// Una riga è insufficiente quando la verifica di giacenza sul suo SKU dice <c>available = false</c>.
    /// Una riga senza verifica non è una prova di indisponibilità: non genera da sola una richiesta di approvazione.
    /// </summary>
    private static bool IsInsufficient(IReadOnlyList<StockCheckDto> stock, OrderLineInput line) =>
        stock.Any(check => string.Equals(check.Sku, line.Sku, StringComparison.OrdinalIgnoreCase) && !check.Available);
}
