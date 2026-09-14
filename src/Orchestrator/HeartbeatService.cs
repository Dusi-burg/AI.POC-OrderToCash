namespace O2C.Orchestrator;

/// <summary>
/// Segnale di vita dello scaffolding di Fase 0: sostituito dal consumer dei deal nelle fasi successive.
/// </summary>
public sealed class HeartbeatService(ILogger<HeartbeatService> logger, IHostEnvironment environment) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            logger.LogInformation("{ServiceName} heartbeat", environment.ApplicationName);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
