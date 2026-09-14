using O2C.Orchestrator;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHostedService<HeartbeatService>();

var host = builder.Build();

host.LogConnectionStringPresence("sql", "rabbitmq");

host.Run();
