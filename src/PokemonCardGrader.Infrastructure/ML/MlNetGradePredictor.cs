using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

public sealed class MlNetGradePredictor(string modelsPath, ILogger<MlNetGradePredictor> logger) : IMlGradePredictor
{
    private readonly MLContext _mlContext = new();
    private readonly ConcurrentDictionary<GradingCompany, Lazy<PredictionEngine<GradeInput, GradePrediction>?>> _engines = new();

    public float? Predict(GradingCompany company, ConditionScores scores)
    {
        var engine = _engines.GetOrAdd(company, c => new Lazy<PredictionEngine<GradeInput, GradePrediction>?>(() => LoadEngine(c)));

        if (engine.Value is null)
            return null;

        var input = new GradeInput
        {
            CenteringLRFront = (float)scores.Centering.LeftRightFront,
            CenteringTBFront = (float)scores.Centering.TopBottomFront,
            CenteringLRBack = (float)scores.Centering.LeftRightBack,
            CenteringTBBack = (float)scores.Centering.TopBottomBack,
            Corners = (float)scores.Corners,
            Edges = (float)scores.Edges,
            Surface = (float)scores.Surface
        };

        var prediction = engine.Value.Predict(input);
        return prediction.Score;
    }

    public void InvalidateModel(GradingCompany company)
    {
        _engines.TryRemove(company, out _);
    }

    private PredictionEngine<GradeInput, GradePrediction>? LoadEngine(GradingCompany company)
    {
        var modelPath = Path.Combine(modelsPath, $"{company}.mlnet");
        if (!File.Exists(modelPath))
        {
            logger.LogDebug("No ML model found for {Company} at {Path}.", company, modelPath);
            return null;
        }

        try
        {
            var model = _mlContext.Model.Load(modelPath, out _);
            logger.LogInformation("Loaded ML model for {Company}.", company);
            return _mlContext.Model.CreatePredictionEngine<GradeInput, GradePrediction>(model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load ML model for {Company}.", company);
            return null;
        }
    }
}
