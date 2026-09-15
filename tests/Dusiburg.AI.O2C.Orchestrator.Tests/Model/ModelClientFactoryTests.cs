using Dusiburg.AI.O2C.Orchestrator.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaSharp.Models;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Model;

/// <summary>Selezione del provider solo da configurazione (3.9, M23); nessuna chiamata al modello.</summary>
public class ModelClientFactoryTests
{
    [Test]
    public void Create_Anthropic_UsesConfiguredModelWithoutSampling()
    {
        //SETUP
        var factory = Factory(new() { ["MODEL_PROVIDER"] = "anthropic", ["ANTHROPIC_API_KEY"] = "test-key", ["ANTHROPIC_MODEL"] = "claude-sonnet-5" });

        //SUT
        using var model = factory.Create();

        Assert.That((model.Provider, model.ModelId), Is.EqualTo((ModelProviders.Anthropic, "claude-sonnet-5")));
        Assert.That(model.DefaultOptions.Temperature, Is.Null);
    }

    [Test]
    public void Create_WithoutProvider_DefaultsToAnthropicSonnet5()
    {
        //SETUP
        var factory = Factory(new() { ["ANTHROPIC_API_KEY"] = "test-key" });

        //SUT
        using var model = factory.Create();

        Assert.That((model.Provider, model.ModelId), Is.EqualTo((ModelProviders.Anthropic, ModelOptions.DefaultAnthropicModel)));
    }

    [Test]
    public void Create_Ollama_DisablesThinkingAndRaisesContext()
    {
        //SETUP
        var factory = Factory(new() { ["MODEL_PROVIDER"] = "ollama", ["OLLAMA_MODEL"] = "qwen3.5:9b" });

        //SUT
        using var model = factory.Create();

        Assert.That((model.Provider, model.ModelId), Is.EqualTo((ModelProviders.Ollama, "qwen3.5:9b")));
        Assert.That(model.DefaultOptions.Temperature, Is.EqualTo(0.1f));
        Assert.That(model.DefaultOptions.AdditionalProperties?[OllamaOption.Think.Name], Is.EqualTo(false));
        Assert.That(model.DefaultOptions.AdditionalProperties?[OllamaOption.NumCtx.Name], Is.EqualTo(ModelOptions.DefaultOllamaContextLength));
    }

    [TestCase("openai")]
    [TestCase("azure-openai")]
    public void Create_UnknownProvider_Throws(string provider)
    {
        //SETUP
        var factory = Factory(new() { ["MODEL_PROVIDER"] = provider, ["ANTHROPIC_API_KEY"] = "test-key" });

        //SUT
        Assert.That(() => factory.Create(), Throws.InvalidOperationException.With.Message.Contains("MODEL_PROVIDER"));
    }

    [Test]
    public void Create_AnthropicWithoutKey_Throws()
    {
        //SETUP
        var factory = Factory(new() { ["MODEL_PROVIDER"] = "anthropic" });

        //SUT
        Assert.That(() => factory.Create(), Throws.InvalidOperationException.With.Message.Contains("ANTHROPIC_API_KEY"));
    }

    private static ModelClientFactory Factory(Dictionary<string, string?> settings) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLoggerFactory.Instance);
}
