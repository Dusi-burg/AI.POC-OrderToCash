using Dusiburg.AI.O2C.Erp.Api.Customers;
using Dusiburg.AI.O2C.Erp.Api.Dev;
using Dusiburg.AI.O2C.Erp.Api.Orders;
using Dusiburg.AI.O2C.Erp.Api.Stock;
using Dusiburg.AI.O2C.Erp.Data;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Schema erp del database O2C, creato da tools/Dusiburg.AI.O2C.DbInit (D30): qui nessuna creazione né migrazione.
// L'integrazione Aspire aggiunge tracce SQL, health check del database e retry sugli errori transitori.
builder.AddSqlServerDbContext<ErpDbContext>("sql");

builder.Services.AddProblemDetails(ToolProblems.Configure);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<OrderService>();

var app = builder.Build();

app.UseCorrelationId();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.O2C.Erp.Api", phase = 1 });

app.MapGroup("/api")
    .MapCustomerEndpoints()
    .MapStockEndpoints()
    .MapOrderEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapErpDevEndpoints();
}

app.LogConnectionStringPresence("sql");

app.Run();
