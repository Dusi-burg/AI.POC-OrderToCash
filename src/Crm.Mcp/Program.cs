var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.UseCorrelationId();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "O2C.Crm.Mcp", phase = 0, status = "scaffolding" });

app.LogConnectionStringPresence("sql", "rabbitmq");

app.Run();
