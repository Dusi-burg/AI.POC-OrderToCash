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
    int OllamaContextLength,
    int? OllamaNumGpu)
{
    public const string DefaultAnthropicModel = "claude-sonnet-5";

    public const string DefaultOllamaModel = "qwen3.5:9b";

    /// <summary>Con 8 GB di VRAM Ollama sceglie 4096 token: istruzioni, schemi dei tool e storico non ci stanno.</summary>
    public const int DefaultOllamaContextLength = 16_384;

    /// <summary>
    /// Layer della chat da caricare in GPU (99 = tutti). Da Ollama 0.40 il runner tiene libero un margine di ~1,9 GB di VRAM
    /// e su 8 GB lascia un layer sulla CPU; con il numero esplicito il modello sta tutto in GPU (~+25% di token/s).
    /// </summary>
    public const int DefaultOllamaNumGpu = 99;

    /// <summary>Valore di <c>OLLAMA_NUM_GPU</c> che lascia decidere a Ollama (margine automatico).</summary>
    public const string AutomaticOllamaNumGpu = "auto";

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
            int.TryParse(configuration["OLLAMA_NUM_CTX"], out var contextLength) && contextLength > 0 ? contextLength : DefaultOllamaContextLength,
            ReadOllamaNumGpu(configuration["OLLAMA_NUM_GPU"]));
    }

    private static int? ReadOllamaNumGpu(string? value)
    {
        var text = NonEmpty(value);

        if (text is null)
        {
            return DefaultOllamaNumGpu;
        }

        if (text.Equals(AutomaticOllamaNumGpu, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(text, out var layers) && layers >= 0 ? layers : DefaultOllamaNumGpu;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
