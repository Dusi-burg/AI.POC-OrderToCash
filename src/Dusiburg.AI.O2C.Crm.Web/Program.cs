using System.Text.Encodings.Web;
using System.Text.Unicode;
using Dusiburg.AI.O2C.Crm.Web.Api;
using Dusiburg.AI.O2C.ServiceDefaults.Portal;
using Microsoft.Extensions.WebEncoders;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Crm.Mcp tramite service discovery (AppHost: WithReference(crmMcp)); nessun accesso diretto allo schema crm (D58).
// La chiusura non si ritenta: un secondo tentativo dopo un 503 risponderebbe 409 e nasconderebbe l'esito vero (G6.3).
// Il resilience handler standard arriva da ServiceDefaults, registrato per tutti i client: le sue opzioni non portano il nome
// del client, quindi la regola vale per tutti i client di questa app (che ne ha uno solo).
builder.Services.AddHttpClient<CrmApiClient>(CrmApiClient.HttpClientName, (services, client) =>
    client.BaseAddress = new Uri(
        services.GetRequiredService<IConfiguration>()[CrmApiClient.BaseAddressConfigurationKey] ?? CrmApiClient.DefaultBaseAddress));

builder.Services.ConfigureAll<HttpStandardResilienceOptions>(options => options.Retry.DisableForUnsafeHttpMethods());

builder.Services.AddScoped<CrmDashboardService>();
builder.Services.AddPortalLinks(builder.Configuration);
// Testi in italiano: nelle pagine le lettere accentate restano leggibili invece di diventare entità numeriche.
builder.Services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
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

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

/// <summary>Tipo di riferimento dell'assembly per <c>WebApplicationFactory</c> nei test (come <c>ApprovalsWebEntryPoint</c>).</summary>
public sealed class CrmWebEntryPoint;
