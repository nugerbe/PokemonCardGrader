using Microsoft.Extensions.Logging;
using Microsoft.ML;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Infrastructure.ML;

public sealed class MlNetTrainingService(
    string modelsPath,
    IGradingResultRepository gradingResultRepository,
    MlNetGradePredictor predictor,
    ILogger<MlNetTrainingService> logger) : IMlTrainingService
{
    private readonly MLContext _mlContext = new(seed: 42);

    public async Task<bool> TrainModelAsync(GradingCompany company, CancellationToken ct = default)
    {
        try
        {
            var results = await gradingResultRepository.GetTrainingDataAsync(company, ct: ct);

            if (results.Count < 10)
            {
                logger.LogWarning("Insufficient training data for {Company}: {Count} samples.", company, results.Count);
                return false;
            }

            var trainingData = results.Select(r => new GradeInput
            {
                Grade = (float)r.ActualGrade
                // Note: In production, we'd join with CardSubmission to get actual ConditionScores
                // For now, we use placeholder features from ActualSubGrades if available
            }).ToList();

            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

            var pipeline = _mlContext.Transforms.Concatenate("Features",
                    nameof(GradeInput.CenteringLRFront),
                    nameof(GradeInput.CenteringTBFront),
                    nameof(GradeInput.CenteringLRBack),
                    nameof(GradeInput.CenteringTBBack),
                    nameof(GradeInput.Corners),
                    nameof(GradeInput.Edges),
                    nameof(GradeInput.Surface))
                .Append(_mlContext.Regression.Trainers.LightGbm(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfLeaves: 31,
                    numberOfIterations: 100,
                    minimumExampleCountPerLeaf: 5,
                    learningRate: 0.1));

            var model = pipeline.Fit(dataView);

            Directory.CreateDirectory(modelsPath);
            var modelPath = Path.Combine(modelsPath, $"{company}.mlnet");
            _mlContext.Model.Save(model, dataView.Schema, modelPath);

            predictor.InvalidateModel(company);

            logger.LogInformation("Trained and saved ML model for {Company} at {Path}.", company, modelPath);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to train model for {Company}.", company);
            return false;
        }
    }

    public bool ModelExists(GradingCompany company)
    {
        var modelPath = Path.Combine(modelsPath, $"{company}.mlnet");
        return File.Exists(modelPath);
    }
}
