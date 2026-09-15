namespace Dusiburg.AI.O2C.Orchestrator.Configuration;

/// <summary>
/// CLI lanciata fuori dall'AppHost in Development: i segreti restano in un solo posto, gli user-secrets dell'AppHost.
/// Si leggono le API key e il collegamento OTLP al dashboard, senza mai sovrascrivere valori già presenti
/// (sotto l'AppHost arrivano come variabili d'ambiente e hanno la precedenza).
/// </summary>
internal static class AppHostSecrets
{
    /// <summary><c>UserSecretsId</c> di <c>Dusiburg.AI.O2C.AppHost.csproj</c>.</summary>
    public const string AppHostUserSecretsId = "34bc01f3-9054-44c3-943c-06c5b46a1f88";

    /// <summary>Endpoint OTLP del dashboard da usare dalla CLI (default in appsettings.Development.json).</summary>
    public const string CliOtlpEndpointKey = "O2C_CLI_OTLP_ENDPOINT";

    private static readonly (string Secret, string Setting)[] Mappings =
    [
        ("Parameters:erp-mcp-api-key", "ERP_MCP_API_KEY"),
        ("Parameters:crm-mcp-api-key", "CRM_MCP_API_KEY"),
        ("Parameters:anthropic-api-key", "ANTHROPIC_API_KEY"),
        ("ConnectionStrings:sql", "ConnectionStrings:sql"),
        ("ConnectionStrings:rabbitmq", "ConnectionStrings:rabbitmq"),
    ];

    public static void AddAppHostSecretsForCli(this IConfigurationManager configuration)
    {
        var secrets = new ConfigurationBuilder().AddUserSecrets(AppHostUserSecretsId).Build();
        var values = new Dictionary<string, string?>();

        foreach (var (secret, setting) in Mappings)
        {
            if (string.IsNullOrEmpty(configuration[setting]) && secrets[secret] is { Length: > 0 } value)
            {
                values[setting] = value;
            }
        }

        if (string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            && configuration[CliOtlpEndpointKey] is { Length: > 0 } endpoint
            && secrets["AppHost:OtlpApiKey"] is { Length: > 0 } otlpApiKey)
        {
            values["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint;
            values["OTEL_EXPORTER_OTLP_HEADERS"] = $"x-otlp-api-key={otlpApiKey}";
            values["OTEL_SERVICE_NAME"] = "orchestrator-cli";
        }

        configuration.AddInMemoryCollection(values);
    }
}
