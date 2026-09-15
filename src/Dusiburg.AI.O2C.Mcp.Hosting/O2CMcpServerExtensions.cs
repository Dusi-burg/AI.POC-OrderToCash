using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dusiburg.AI.O2C.Mcp.Hosting.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dusiburg.AI.O2C.Mcp.Hosting;

/// <summary>
/// Composizione comune dei server MCP del POC (D35): trasporto HTTP stateless, API key su <c>/mcp</c>, filtro sulle chiamate ai tool.
/// </summary>
public static class O2CMcpServerExtensions
{
    public const string EndpointPath = "/mcp";

    public const string ApiKeyHeaderName = "X-Api-Key";

    /// <summary>
    /// Opzioni JSON dei tool (argomenti, output strutturato e schemi): le stesse regole dei contratti HTTP (<c>JsonSerializerDefaults.Web</c>),
    /// non quelle dell'SDK. Con le opzioni dell'SDK le proprietà <c>null</c> sparirebbero (<c>{ customer: null }</c> → <c>{}</c>) e il suo
    /// resolver scavalcherebbe <c>StrictStringEnumConverter</c>, accettando gli enum anche come interi (<c>status: 1</c>).
    /// </summary>
    public static JsonSerializerOptions ToolSerializerOptions { get; } = CreateToolSerializerOptions();

    /// <summary>
    /// Registra il server MCP; i tool si aggiungono sul builder restituito (<c>WithTools&lt;T&gt;()</c>).
    /// La API key si legge dalla chiave di configurazione <paramref name="apiKeyConfigurationKey"/>: se manca il servizio non parte.
    /// </summary>
    public static IMcpServerBuilder AddO2CMcpServer(
        this IHostApplicationBuilder builder, string activitySourceName, string apiKeyConfigurationKey)
    {
        builder.Services.AddOptions<McpApiKeyOptions>()
            .Configure<IConfiguration>((options, configuration) => options.ApiKey = configuration[apiKeyConfigurationKey] ?? string.Empty)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                $"{apiKeyConfigurationKey} non configurata: impostarla negli user-secrets dell'AppHost.")
            .ValidateOnStart();

        var activitySource = new ActivitySource(activitySourceName);

        return builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithRequestFilters(filters => filters.AddCallToolFilter(ToolCallFilter.Create(activitySource)));
    }

    /// <summary>Mappa l'endpoint MCP protetto da API key; va chiamato dopo <c>UseCorrelationId()</c>.</summary>
    public static IEndpointConventionBuilder MapO2CMcp(this WebApplication app)
    {
        app.UseWhen(
            static context => context.Request.Path.StartsWithSegments(EndpointPath),
            static branch => branch.UseMiddleware<ApiKeyMiddleware>());

        return app.MapMcp(EndpointPath);
    }

    private static JsonSerializerOptions CreateToolSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        options.MakeReadOnly();

        return options;
    }
}
