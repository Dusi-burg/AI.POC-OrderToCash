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
// La credenziale del modello si aggiunge con il Gate di Fase 3 (M10).
var erpMcpApiKey = builder.AddParameter("erp-mcp-api-key", secret: true);
var crmMcpApiKey = builder.AddParameter("crm-mcp-api-key", secret: true);

// Porte fisse dai launchSettings: Erp.Api 5101, Erp.Mcp 5102, Crm.Mcp 5103, Approvals.Web 5104.
var erpApi = builder.AddProject<Projects.Erp_Api>("erp-api")
    .WithReference(sql)
    .WithHttpHealthCheck("/health");

var erpMcp = builder.AddProject<Projects.Erp_Mcp>("erp-mcp")
    .WithReference(erpApi)
    .WithEnvironment("ERP_MCP_API_KEY", erpMcpApiKey)
    .WithHttpHealthCheck("/health");

var crmMcp = builder.AddProject<Projects.Crm_Mcp>("crm-mcp")
    .WithReference(sql)
    .WithReference(rabbitmq)
    .WithEnvironment("CRM_MCP_API_KEY", crmMcpApiKey)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Orchestrator>("orchestrator")
    .WithReference(sql)
    .WithReference(rabbitmq)
    .WithReference(erpMcp)
    .WithReference(crmMcp)
    .WithEnvironment("ERP_MCP_API_KEY", erpMcpApiKey)
    .WithEnvironment("CRM_MCP_API_KEY", crmMcpApiKey);

builder.AddProject<Projects.Approvals_Web>("approvals-web")
    .WithReference(sql)
    .WithReference(rabbitmq)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
