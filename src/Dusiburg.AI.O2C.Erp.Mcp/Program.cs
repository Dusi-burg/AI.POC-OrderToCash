var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.UseCorrelationId();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.O2C.Erp.Mcp", phase = 0, status = "scaffolding" });

app.Run();
