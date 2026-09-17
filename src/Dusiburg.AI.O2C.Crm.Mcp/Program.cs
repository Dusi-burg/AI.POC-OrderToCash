using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Mcp.Crm;
using Dusiburg.AI.O2C.Crm.Mcp.Deals;
using Dusiburg.AI.O2C.Crm.Mcp.Dev;
using Dusiburg.AI.O2C.Crm.Mcp.Messaging;
using Dusiburg.AI.O2C.Crm.Mcp.Tools;
using Dusiburg.AI.O2C.Crm.Mcp.Views;
using Dusiburg.AI.O2C.Mcp.Hosting;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Dusiburg.AI.O2C.Shared.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Schema crm del database O2C, creato da tools/Dusiburg.AI.O2C.DbInit (D30): qui nessuna creazione né migrazione.
builder.AddSqlServerDbContext<CrmDbContext>("sql");

builder.Services.AddProblemDetails(ToolProblems.Configure);
builder.Services.Configure<ExceptionHandlerOptions>(ToolProblems.ConfigureExceptionHandler);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICrmClient, MockCrmClient>();

// Broker RabbitMQ (D46): connessione dalla connection string "rabbitmq", health check e tracing dell'integrazione Aspire.
builder.AddRabbitMQClient("rabbitmq");
builder.Services.AddSingleton<IDealEventPublisher, DealEventPublisher>();

// API degli utenti del CRM per Crm.Web (Fase 6): viste in lettura e comando di chiusura.
builder.Services.AddScoped<CrmViewQueries>();
builder.Services.AddScoped<DealClosingService>();

// Server MCP stateless su /mcp con API key (CRM_MCP_API_KEY, passata dall'AppHost) e filtro comune sui tool (D35).
builder.AddO2CMcpServer(O2CTelemetry.Sources.McpCrm, "CRM_MCP_API_KEY")
    .WithTools<CrmTools>(O2CMcpServerExtensions.ToolSerializerOptions);

var app = builder.Build();

app.UseCorrelationId();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.O2C.Crm.Mcp", phase = 2, mcp = O2CMcpServerExtensions.EndpointPath });

app.MapO2CMcp();

app.MapCrmApiEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapCrmDevEndpoints();
}

app.LogConnectionStringPresence("sql", "rabbitmq");

app.Run();
