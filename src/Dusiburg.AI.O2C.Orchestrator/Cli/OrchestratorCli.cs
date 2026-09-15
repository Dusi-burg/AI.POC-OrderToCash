using System.CommandLine;
using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;

namespace Dusiburg.AI.O2C.Orchestrator.Cli;

/// <summary>
/// Riga di comando dell'orchestratore (3.5): <c>process --deal D-1001</c> stampa l'esito in JSON.
/// Exit code 0 ordine creato, 1 deal non concluso, 2 errore (configurazione, rete, modello).
/// </summary>
internal static class OrchestratorCli
{
    public const int ExitOrderCreated = 0;

    public const int ExitNotCompleted = 1;

    public const int ExitError = 2;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> InvokeAsync(string[] args, IServiceProvider services)
    {
        var dealOption = new Option<string>("--deal") { Description = "Identificativo del deal CRM, es. D-1001.", Required = true };

        var process = new Command("process", "Elabora un deal con l'agente singolo e crea l'ordine in ERP.") { dealOption };

        process.SetAction(async (parseResult, cancellationToken) =>
        {
            var dealId = parseResult.GetValue(dealOption)!;

            try
            {
                var result = await services.GetRequiredService<DealProcessor>().ProcessAsync(dealId, cancellationToken);

                Console.WriteLine(JsonSerializer.Serialize(result, Output));

                return result.Status == DealStatus.OrderCreated ? ExitOrderCreated : ExitNotCompleted;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await Console.Error.WriteLineAsync($"Errore nell'elaborazione di {dealId}: {exception.Message}");

                return ExitError;
            }
        });

        var root = new RootCommand("Orchestratore Order-to-Cash. Senza argomenti parte come worker.") { process };

        return await root.Parse(args).InvokeAsync();
    }
}
