using System.Text.Encodings.Web;
using System.Text.Unicode;
using Dusiburg.AI.O2C.Erp.Web.Api;
using Dusiburg.AI.O2C.ServiceDefaults.Portal;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Erp.Api tramite service discovery (AppHost: WithReference(erpApi)); nessun accesso diretto allo schema erp (D58).
// Solo letture: il resilience handler di ServiceDefaults può ritentare senza rischi.
builder.Services.AddHttpClient<ErpApiClient>((services, client) =>
    client.BaseAddress = new Uri(
        services.GetRequiredService<IConfiguration>()[ErpApiClient.BaseAddressConfigurationKey] ?? ErpApiClient.DefaultBaseAddress));

builder.Services.AddScoped<ErpDashboardService>();
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

// ERP in sola lettura (D58): una POST verso una pagina senza handler verrebbe comunque eseguita come la GET, quindi si
// rifiuta a monte ogni metodo diverso da GET e HEAD.
app.Use(async (context, next) =>
{
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Allow = "GET, HEAD";

        return;
    }

    await next(context);
});

app.UseRouting();

app.UseAuthorization();

app.MapDefaultEndpoints();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

/// <summary>Tipo di riferimento dell'assembly per <c>WebApplicationFactory</c> nei test (come <c>ApprovalsWebEntryPoint</c>).</summary>
public sealed class ErpWebEntryPoint;
