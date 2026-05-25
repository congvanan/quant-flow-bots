using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Infrastructure.Trading;

namespace QuantFlowBots.Worker.Workers;

public sealed class VolumeSpikeWorker(
    IMarketEventBus marketBus,
    VolumeSpikeDetector detector,
    IVolumeSpikeBus spikeBus,
    ILogger<VolumeSpikeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("VolumeSpikeWorker started. Threshold: 3x avg, $200K min, ratio>=0.60 buy / <=0.40 sell.");
        await foreach (var evt in marketBus.Klines.ReadAllAsync(stoppingToken))
        {
            try
            {
                var spike = detector.Process(evt);
                if (spike is not null)
                {
                    await spikeBus.PublishAsync(spike, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "VolumeSpikeWorker failed processing {Symbol}", evt.Symbol);
            }
        }
    }
}
