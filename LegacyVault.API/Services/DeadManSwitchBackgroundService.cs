using LegacyVault.BLL.Services;

namespace LegacyVault.API.Services;

public sealed class DeadManSwitchBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DeadManSwitchBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var managementService = scope.ServiceProvider.GetRequiredService<ManagementService>();
                var triggered = await managementService.ProcessOverdueProofOfLives(DateTime.UtcNow, stoppingToken);
                if (triggered > 0)
                {
                    logger.LogInformation("DeadManSwitch automatically triggered {Count} handover request(s).", triggered);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process overdue proof-of-life checks.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

