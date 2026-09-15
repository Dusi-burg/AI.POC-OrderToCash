namespace Dusiburg.AI.O2C.Orchestrator.Model;

public static class ModelProviders
{
    public const string Anthropic = "anthropic";

    public const string Ollama = "ollama";
}

/// <summary>Configurazione del modello (§10, M23), letta e validata a ogni creazione del client.</summary>
public sealed record ModelOptions(
    string Provider,
    string? AnthropicApiKey,
    string AnthropicModel,
    Uri OllamaEndpoint,
    string OllamaModel,
    int OllamaContextLength)
{
    public const string DefaultAnthropicModel = "claude-sonnet-5";

    public const string DefaultOllamaModel = "qwen3.5:9b";

    /// <summary>Con 8 GB di VRAM Ollama sceglie 4096 token: istruzioni, schemi dei tool e storico non ci stanno.</summary>
    public const int DefaultOllamaContextLength = 16_384;

    public static ModelOptions FromConfiguration(IConfiguration configuration)
    {
        var provider = (configuration["MODEL_PROVIDER"] ?? ModelProviders.Anthropic).Trim().ToLowerInvariant();

        if (provider is not (ModelProviders.Anthropic or ModelProviders.Ollama))
        {
            throw new InvalidOperationException(
                $"MODEL_PROVIDER '{provider}' non supportato: usare '{ModelProviders.Anthropic}' oppure '{ModelProviders.Ollama}'.");
        }

        var apiKey = configuration["ANTHROPIC_API_KEY"];

        if (provider == ModelProviders.Anthropic && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "ANTHROPIC_API_KEY non configurata: impostare Parameters:anthropic-api-key negli user-secrets dell'AppHost.");
        }

        return new ModelOptions(
            provider,
            apiKey,
            NonEmpty(configuration["ANTHROPIC_MODEL"]) ?? DefaultAnthropicModel,
            new Uri(NonEmpty(configuration["OLLAMA_ENDPOINT"]) ?? "http://localhost:11434"),
            NonEmpty(configuration["OLLAMA_MODEL"]) ?? DefaultOllamaModel,
            int.TryParse(configuration["OLLAMA_NUM_CTX"], out var contextLength) && contextLength > 0 ? contextLength : DefaultOllamaContextLength);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
