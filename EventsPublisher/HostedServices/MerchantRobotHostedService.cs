namespace EventsPublisher.HostedServices;

public sealed class MerchantRobotHostedService(
    MerchantRobot merchantRobot,
    IConfiguration configuration,
    ILogger<MerchantRobotHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupDelayMs = configuration.GetValue<int?>("Publisher:StartupDelayMs") ?? 10000;
        if (startupDelayMs > 0)
        {
            logger.LogInformation("Waiting {StartupDelayMs}ms before starting merchant robot", startupDelayMs);
            await Task.Delay(startupDelayMs, stoppingToken);
        }

        logger.LogInformation("Merchant robot hosted service started");
        await merchantRobot.Loop(stoppingToken);
    }
}

