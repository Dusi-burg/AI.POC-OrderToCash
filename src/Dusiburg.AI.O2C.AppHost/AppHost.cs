var builder = DistributedApplication.CreateBuilder(args);

// Risorse esterne (D8): nessun container gestito da Aspire.
// Valori negli user-secrets dell'AppHost: ConnectionStrings:sql, ConnectionStrings:rabbitmq.
var sql = builder.AddConnectionString("sql");
var rabbitmq = builder.AddConnectionString("rabbitmq");

// RabbitMQ gira nel container Docker dentro WSL. Senza sessioni aperte WSL si spegne da sola dopo pochi
// secondi e il broker con lei: questa risorsa tiene aperta una sessione finché è in esecuzione l'AppHost
// (e mostra i log del broker nel dashboard). Distro e container sono configurabili.
builder.AddExecutable(
    "rabbitmq-wsl",
    "wsl",
    builder.AppHostDirectory,
    "-d", builder.Configuration["RabbitMq:WslDistro"] ?? "Ubuntu-26.04",
    "--", "docker", "start", "--attach", builder.Configuration["RabbitMq:Container"] ?? "rabbitmq");

// API key dei server MCP, usate dalla Fase 2 (Parameters:erp-mcp-api-key, Parameters:crm-mcp-api-key).
var erpMcpApiKey = builder.AddParameter("erp-mcp-api-key", secret: true);
var crmMcpApiKey = builder.AddParameter("crm-mcp-api-key", secret: true);

// Chiave del modello Claude (D40, M10): Parameters:anthropic-api-key. La CLI dell'orchestratore la legge dagli stessi user-secrets.
var anthropicApiKey = builder.AddParameter("anthropic-api-key", secret: true);

// Porte fisse dai launchSettings: Erp.Api 5101, Erp.Mcp 5102, Crm.Mcp 5103, Approvals.Web 5104, Crm.Web 5105, Erp.Web 5106.
var erpApi = builder.AddProject<Projects.Dusiburg_AI_O2C_Erp_Api>("erp-api")
    .WithReference(sql)
    .WithHttpHealthCheck("/health");

var erpMcp = builder.AddProject<Projects.Dusiburg_AI_O2C_Erp_Mcp>("erp-mcp")
    .WithReference(erpApi)
    .WithEnvironment("ERP_MCP_API_KEY", erpMcpApiKey)
    .WithHttpHealthCheck("/health");

var crmMcp = builder.AddProject<Projects.Dusiburg_AI_O2C_Crm_Mcp>("crm-mcp")
    .WithReference(sql)
    .WithReference(rabbitmq)
    .WithEnvironment("CRM_MCP_API_KEY", crmMcpApiKey)
    .WithHttpHealthCheck("/health");

var orchestrator = builder.AddProject<Projects.Dusiburg_AI_O2C_Orchestrator>("orchestrator")
    .WithReference(sql)
    .WithReference(rabbitmq)
    .WithReference(erpMcp)
    .WithReference(crmMcp)
    .WithEnvironment("ERP_MCP_API_KEY", erpMcpApiKey)
    .WithEnvironment("CRM_MCP_API_KEY", crmMcpApiKey)
    .WithEnvironment("ANTHROPIC_API_KEY", anthropicApiKey);

// Manopole della demo (Fase 5): si impostano sull'AppHost — riga di comando, user-secrets o variabili d'ambiente —
// e arrivano al servizio che le legge. Senza valore vale il default del codice.
orchestrator.WithConfigurationEnvironment(
    builder,
    "MODEL_PROVIDER",
    "ANTHROPIC_MODEL",
    "OLLAMA_MODEL",
    "OLLAMA_ENDPOINT",
    "O2C_AGENT_MODE",
    "APPROVAL_THRESHOLD_EUR",
    "APPROVAL_TIMEOUT_HOURS",
    "APPROVAL_SWEEP_MINUTES");

var approvals = builder.AddProject<Projects.Dusiburg_AI_O2C_Approvals_Web>("approvals-web")
    .WithReference(sql)
    .WithReference(rabbitmq)
    .WithHttpHealthCheck("/health");

approvals.WithConfigurationEnvironment(builder, "Approvals__ApproverUpn");

// UI dei due sistemi (Fase 6, D58): solo HTTP verso il servizio proprietario dei dati, nessun riferimento a sql o rabbitmq.
var crmWeb = builder.AddProject<Projects.Dusiburg_AI_O2C_Crm_Web>("crm-web")
    .WithReference(crmMcp)
    .WithHttpHealthCheck("/health");

var erpWeb = builder.AddProject<Projects.Dusiburg_AI_O2C_Erp_Web>("erp-web")
    .WithReference(erpApi)
    .WithHttpHealthCheck("/health");

// Collegamenti fra le tre UI (G6.8) dagli endpoint delle risorse: nessun indirizzo da configurare a mano.
foreach (var portal in new[] { approvals, crmWeb, erpWeb })
{
    portal
        .WithEnvironment("Links__CrmWeb", crmWeb.GetEndpoint("http"))
        .WithEnvironment("Links__ErpWeb", erpWeb.GetEndpoint("http"))
        .WithEnvironment("Links__ApprovalsWeb", approvals.GetEndpoint("http"));
}

builder.Build().Run();

internal static class AppHostExtensions
{
    /// <summary>
    /// Inoltra al servizio le impostazioni presenti nella configurazione dell'AppHost, saltando quelle non valorizzate:
    /// così una variabile della demo si imposta in un solo posto e non va replicata in ogni progetto.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithConfigurationEnvironment(
        this IResourceBuilder<ProjectResource> resource, IDistributedApplicationBuilder builder, params string[] names)
    {
        foreach (var name in names)
        {
            // Nelle variabili d'ambiente la sezione si scrive con il doppio underscore; nella configurazione con i due punti.
            if (builder.Configuration[name.Replace("__", ":", StringComparison.Ordinal)] is { Length: > 0 } value)
            {
                resource.WithEnvironment(name, value);
            }
        }

        return resource;
    }
}
