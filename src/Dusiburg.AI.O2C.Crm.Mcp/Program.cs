using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Mcp.Crm;
using Dusiburg.AI.O2C.Crm.Mcp.Dev;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Schema crm del database O2C, creato da tools/Dusiburg.AI.O2C.DbInit (D30): qui nessuna creazione né migrazione.
builder.AddSqlServerDbContext<CrmDbContext>("sql");

builder.Services.AddProblemDetails(ToolProblems.Configure);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICrmClient, MockCrmClient>();

var app = builder.Build();

app.UseCorrelationId();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.O2C.Crm.Mcp", phase = 1 });

if (app.Environment.IsDevelopment())
{
    app.MapCrmDevEndpoints();
}

app.LogConnectionStringPresence("sql", "rabbitmq");

app.Run();
