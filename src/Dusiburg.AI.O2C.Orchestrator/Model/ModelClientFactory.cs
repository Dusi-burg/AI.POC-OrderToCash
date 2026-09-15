using Anthropic;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace Dusiburg.AI.O2C.Orchestrator.Model;

/// <summary>Modello pronto per un run: client con logging e telemetria, opzioni di default del provider.</summary>
public sealed record ModelClient(IChatClient ChatClient, string Provider, string ModelId, ChatOptions DefaultOptions) : IDisposable
{
    public void Dispose() => ChatClient.Dispose();
}

public interface IModelClientFactory
{
    ModelClient Create();
}

/// <summary>
/// Unico punto che conosce i provider (3.1, §3.3): il resto dell'orchestratore vede solo <see cref="IChatClient"/>.
/// </summary>
public sealed class ModelClientFactory(IConfiguration configuration, ILoggerFactory loggerFactory) : IModelClientFactory
{
    public const string TelemetrySourceName = O2CTelemetry.Sources.Orchestrator + ".Model";

    private const int MaxOutputTokens = 8_192;

    public ModelClient Create()
    {
        var options = ModelOptions.FromConfiguration(configuration);

        var (inner, modelId, defaults) = options.Provider == ModelProviders.Anthropic
            ? CreateAnthropic(options)
            : CreateOllama(options);

        var chatClient = inner
            .AsBuilder()
            .UseLogging(loggerFactory)
            .UseOpenTelemetry(loggerFactory, TelemetrySourceName)
            .Build();

        return new ModelClient(chatClient, options.Provider, modelId, defaults);
    }

    /// <summary>
    /// Haiku 4.5 non supporta l'adaptive thinking dei modelli 4.6+: con la modalità Extended e senza <c>ChatOptions.Reasoning</c>
    /// il client non invia alcuna configurazione di thinking.
    /// </summary>
    internal static AnthropicThinkingMode ThinkingModeFor(string model) =>
        model.StartsWith("claude-haiku-4-5", StringComparison.OrdinalIgnoreCase)
            ? AnthropicThinkingMode.Extended
            : AnthropicThinkingMode.Adaptive;

    private static (IChatClient Client, string ModelId, ChatOptions Defaults) CreateAnthropic(ModelOptions options)
    {
        var client = new AnthropicClient { ApiKey = options.AnthropicApiKey }
            .AsIChatClient(options.AnthropicModel, MaxOutputTokens, ThinkingModeFor(options.AnthropicModel));

        // Claude Sonnet 5 rifiuta temperature e top_p (HTTP 400): il determinismo viene da istruzioni, guardia e fatti dei tool.
        return (client, options.AnthropicModel, new ChatOptions());
    }

    private static (IChatClient Client, string ModelId, ChatOptions Defaults) CreateOllama(ModelOptions options)
    {
        // HttpClient dedicato, senza la resilienza di ServiceDefaults: i suoi timeout (10 s per tentativo) non reggono la generazione locale.
        var http = new HttpClient { BaseAddress = options.OllamaEndpoint, Timeout = TimeSpan.FromMinutes(10) };
        var client = new OllamaApiClient(http, options.OllamaModel);

        var defaults = new ChatOptions { Temperature = 0.1f, MaxOutputTokens = MaxOutputTokens };

        // Qwen 3.5 con il thinking attivo a volte lascia la chiamata al tool dentro il ragionamento.
        defaults.AddOllamaOption(OllamaOption.Think, false);
        defaults.AddOllamaOption(OllamaOption.NumCtx, options.OllamaContextLength);

        return (client, options.OllamaModel, defaults);
    }
}
