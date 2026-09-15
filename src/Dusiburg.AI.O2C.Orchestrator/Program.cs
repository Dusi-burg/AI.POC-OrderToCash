using Dusiburg.AI.O2C.Orchestrator;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Cli;
using Dusiburg.AI.O2C.Orchestrator.Configuration;
using Dusiburg.AI.O2C.Orchestrator.Model;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Microsoft.Extensions.Logging.Console;

// Con argomenti è la riga di comando (G3.4, es. "process --deal D-1001"); senza argomenti è il worker avviato dall'AppHost.
var cliMode = args.Length > 0;

var builder = Host.CreateApplicationBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Prima di AddServiceDefaults: può impostare anche l'endpoint OTLP del dashboard per la CLI.
    builder.Configuration.AddAppHostSecretsForCli();
}

builder.AddServiceDefaults();

if (cliMode)
{
    // La console resta leggibile per l'esito; i log completi vanno comunque al dashboard via OTLP.
    builder.Logging.AddFilter<ConsoleLoggerProvider>(level => level >= LogLevel.Warning);
}

builder.Services.AddSingleton<IModelClientFactory, ModelClientFactory>();
builder.Services.AddSingleton<McpToolCatalog>();
builder.Services.AddSingleton<IToolCatalog>(services => services.GetRequiredService<McpToolCatalog>());
builder.Services.AddSingleton<SingleOrderAgent>();
builder.Services.AddSingleton<DealProcessor>();

if (!cliMode)
{
    builder.Services.AddHostedService<HeartbeatService>();
}

using var host = builder.Build();

if (cliMode)
{
    await host.StartAsync();

    try
    {
        return await OrchestratorCli.InvokeAsync(args, host.Services);
    }
    finally
    {
        await host.StopAsync();
    }
}

host.LogConnectionStringPresence("sql", "rabbitmq");

await host.RunAsync();

return 0;
