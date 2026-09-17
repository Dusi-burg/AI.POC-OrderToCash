using Dusiburg.AI.O2C.Approvals.Web.Approvals;
using Dusiburg.AI.O2C.Orchestration.Data;
using Dusiburg.AI.O2C.ServiceDefaults.Portal;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Schema orch del database O2C, condiviso con l'orchestratore (D45): qui nessuna creazione né migrazione.
builder.AddSqlServerDbContext<OrchestrationDbContext>("sql");

// Le decisioni si annunciano su approval-decided (5.4): per l'orchestratore sono un acceleratore della ripresa (G5.3).
builder.AddRabbitMQClient("rabbitmq");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ApprovalRepository>();
builder.Services.AddSingleton<IApprovalDecisionPublisher, RabbitMqApprovalDecisionPublisher>();
builder.Services.AddScoped<ApprovalDecisionService>();
builder.Services.AddProblemDetails(ToolProblems.Configure);
builder.Services.AddPortalLinks(builder.Configuration);
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseCorrelationId();

app.UseRouting();

app.UseAuthorization();

app.MapDefaultEndpoints();

app.MapApprovalEndpoints();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.LogConnectionStringPresence("sql", "rabbitmq");

app.Run();

/// <summary>
/// Tipo di riferimento dell'assembly per <c>WebApplicationFactory</c> nei test: il <c>Program</c> generato dai
/// top-level statements avrebbe lo stesso nome di quello dell'orchestratore, che i test referenziano insieme a questo.
/// </summary>
public sealed class ApprovalsWebEntryPoint;
