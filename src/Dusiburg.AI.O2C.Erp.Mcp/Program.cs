using Dusiburg.AI.O2C.Erp.Mcp.Erp;
using Dusiburg.AI.O2C.Erp.Mcp.Tools;
using Dusiburg.AI.O2C.Mcp.Hosting;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Dusiburg.AI.O2C.Shared.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails(ToolProblems.Configure);

// Erp.Api tramite service discovery (AppHost: WithReference(erpApi)); l'indirizzo si può sovrascrivere, ad esempio nei test.
builder.Services.AddHttpClient<ErpApiClient>((services, client) =>
    client.BaseAddress = new Uri(
        services.GetRequiredService<IConfiguration>()[ErpApiClient.BaseAddressConfigurationKey] ?? ErpApiClient.DefaultBaseAddress));

// Server MCP stateless su /mcp con API key (ERP_MCP_API_KEY, passata dall'AppHost) e filtro comune sui tool (D35).
builder.AddO2CMcpServer(O2CTelemetry.Sources.McpErp, "ERP_MCP_API_KEY")
    .WithTools<ErpTools>(O2CMcpServerExtensions.ToolSerializerOptions);

var app = builder.Build();

app.UseCorrelationId();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.O2C.Erp.Mcp", phase = 2, mcp = O2CMcpServerExtensions.EndpointPath });

app.MapO2CMcp();

app.Run();
