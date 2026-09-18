using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Configuration;
using Dusiburg.AI.O2C.Orchestrator.Model;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dusiburg.AI.O2C.PromptReplay;

/// <summary>Impostazioni della riga di comando.</summary>
public sealed record ReplaySettings(int Repeat, bool ApplyFilter, bool CapturedInstructions, IReadOnlySet<string> Cases);

/// <summary>
/// Rimanda al modello le richieste catturate in <c>Cases/</c> e controlla la prima chiamata di ogni risposta (D62).
/// Il modello si configura come per l'orchestratore (<c>MODEL_PROVIDER</c>, <c>OLLAMA_MODEL</c>, <c>ANTHROPIC_MODEL</c>;
/// chiavi dagli user-secrets dell'AppHost). Per default usa le istruzioni attuali degli agenti e il filtro della catena.
/// <code>
/// dotnet run --project tools/Dusiburg.AI.O2C.PromptReplay -- [--repeat N] [--no-filter] [--captured-instructions] [--case nome]...
/// </code>
/// Exit code 0 se ogni caso ha dato sempre la chiamata attesa, 1 altrimenti, 2 per un errore.
/// </summary>
public static class ReplayProgram
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            ReplaySettings settings = ParseArguments(args);

            var configuration = new ConfigurationManager();
            configuration.AddEnvironmentVariables();
            configuration.AddAppHostSecretsForCli();

            var factory = new ModelClientFactory(configuration, NullLoggerFactory.Instance);
            string casesDirectory = Path.Combine(AppContext.BaseDirectory, "Cases");
            List<ReplayCase> cases = LoadCases(casesDirectory, settings.Cases);

            using ModelClient model = factory.Create();
            Console.WriteLine($"Modello {model.Provider}/{model.ModelId} · ripetizioni {settings.Repeat} · filtro {(settings.ApplyFilter ? "attivo" : "disattivo")} · istruzioni {(settings.CapturedInstructions ? "catturate" : "attuali")}");

            var failures = 0;

            foreach (ReplayCase replayCase in cases)
            {
                Dictionary<string, int> outcomes = await ReplayAsync(model, replayCase, casesDirectory, settings);
                int passed = outcomes.GetValueOrDefault(replayCase.ExpectedFirstCall);
                bool ok = passed == settings.Repeat;
                failures += ok ? 0 : 1;

                string detail = string.Join(", ", outcomes.OrderByDescending(o => o.Value).Select(o => $"{o.Key}={o.Value}"));
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {replayCase.Name}: {passed}/{settings.Repeat} {replayCase.ExpectedFirstCall} ({detail})");
            }

            return failures == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Errore: {exception.Message}");

            return 2;
        }
    }

    private static async Task<Dictionary<string, int>> ReplayAsync(ModelClient model, ReplayCase replayCase, string casesDirectory, ReplaySettings settings)
    {
        CapturedRequest request = CapturedRequest.Load(Path.Combine(casesDirectory, replayCase.Capture));

        if (!settings.CapturedInstructions)
        {
            request = request.WithCurrentAgentDefinition();
        }

        if (replayCase.ReplaceLastText is { } text)
        {
            request = request.WithLastText(text);
        }

        List<ChatMessage> messages = settings.ApplyFilter
            ? ForeignAgentTextFilter.Filter(request.Messages, request.Agent, WorkflowAgents.Names)
            : [.. request.Messages];

        ChatOptions options = model.DefaultOptions.Clone();
        options.Instructions = request.Instructions;
        options.Tools = [.. request.Tools];
        options.AllowMultipleToolCalls = request.AllowMultipleToolCalls;

        var outcomes = new Dictionary<string, int>();

        for (var attempt = 0; attempt < settings.Repeat; attempt++)
        {
            ChatResponse response = await model.ChatClient.GetResponseAsync(messages, options);
            string firstCall = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().FirstOrDefault()?.Name ?? "(solo testo)";
            outcomes[firstCall] = outcomes.GetValueOrDefault(firstCall) + 1;
        }

        return outcomes;
    }

    private static List<ReplayCase> LoadCases(string casesDirectory, IReadOnlySet<string> selected)
    {
        List<ReplayCase> cases = JsonSerializer.Deserialize<List<ReplayCase>>(
            File.ReadAllText(Path.Combine(casesDirectory, "cases.json")), JsonSerializerOptions.Web) ?? [];

        if (selected.Count == 0)
        {
            return cases;
        }

        List<string> unknown = [.. selected.Where(name => cases.All(c => c.Name != name))];

        return unknown.Count == 0
            ? [.. cases.Where(c => selected.Contains(c.Name))]
            : throw new ArgumentException($"Casi sconosciuti: {string.Join(", ", unknown)}.");
    }

    private static ReplaySettings ParseArguments(string[] args)
    {
        var repeat = 5;
        var applyFilter = true;
        var capturedInstructions = false;
        var cases = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repeat" when i + 1 < args.Length && int.TryParse(args[i + 1], out int value) && value > 0:
                    repeat = value;
                    i++;
                    break;
                case "--no-filter":
                    applyFilter = false;
                    break;
                case "--captured-instructions":
                    capturedInstructions = true;
                    break;
                case "--case" when i + 1 < args.Length:
                    cases.Add(args[++i]);
                    break;
                default:
                    throw new ArgumentException($"Argomento non valido: {args[i]}");
            }
        }

        return new ReplaySettings(repeat, applyFilter, capturedInstructions, cases);
    }
}
