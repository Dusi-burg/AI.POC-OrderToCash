var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

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

app.LogConnectionStringPresence("sql", "rabbitmq");

app.Run();
