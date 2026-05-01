using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.Workers;

public sealed class MlRetrainingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MlRetrainingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private const int MinTrainingSamples = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MlRetrainingWorker started. Check interval: {Interval}.", CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRetrainAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during ML retraining check.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckAndRetrainAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var gradingResultRepository = scope.ServiceProvider.GetRequiredService<IGradingResultRepository>();
        var trainingService = scope.ServiceProvider.GetRequiredService<IMlTrainingService>();

        foreach (var company in Enum.GetValues<GradingCompany>())
        {
            var count = await gradingResultRepository.GetCountByCompanyAsync(company, ct);

            if (count < MinTrainingSamples)
            {
                logger.LogDebug(
                    "Skipping {Company}: only {Count}/{Min} training samples.",
                    company, count, MinTrainingSamples);
                continue;
            }

            logger.LogInformation(
                "Training ML model for {Company} with {Count} samples.",
                company, count);

            var success = await trainingService.TrainModelAsync(company, ct);

            if (success)
                logger.LogInformation("Successfully trained model for {Company}.", company);
            else
                logger.LogWarning("Failed to train model for {Company}.", company);
        }
    }
}
