using Microsoft.Extensions.Logging;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.GradingEngines;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

public sealed class GradeEstimationService(
    GradingRuleEngineFactory ruleEngineFactory,
    IMlGradePredictor mlPredictor,
    IGradingResultRepository gradingResultRepository,
    ILogger<GradeEstimationService> logger) : IGradeEstimationService
{
    private const int MlPrimaryThreshold = 200;
    private const double MaxDeviationWarning = 2.0;

    public async Task<List<GradeEstimate>> EstimateAllCompaniesAsync(
        Guid cardSubmissionId, ConditionScores scores, CancellationToken ct = default)
    {
        var estimates = new List<GradeEstimate>();

        foreach (var engine in ruleEngineFactory.GetAllEngines())
        {
            var ruleResult = engine.EstimateGrade(scores);
            var mlPrediction = mlPredictor.Predict(engine.Company, scores);
            var sampleCount = await gradingResultRepository.GetCountByCompanyAsync(engine.Company, ct);

            double finalGrade;
            double confidence;
            bool isRuleBased;
            string label;
            Dictionary<string, double> subGrades;

            if (mlPrediction is not null && sampleCount >= MlPrimaryThreshold)
            {
                // ML is primary
                finalGrade = Math.Round(mlPrediction.Value, 1);
                confidence = 0.85;
                isRuleBased = false;
                label = ruleResult.Label;
                subGrades = ruleResult.SubGrades;

                if (Math.Abs(finalGrade - ruleResult.Grade) > MaxDeviationWarning)
                {
                    logger.LogWarning(
                        "ML grade ({MlGrade}) deviates from rule-based ({RuleGrade}) by >{MaxDev} for {Company}.",
                        finalGrade, ruleResult.Grade, MaxDeviationWarning, engine.Company);
                }
            }
            else
            {
                // Rule-based (possibly with ML as reference)
                finalGrade = ruleResult.Grade;
                confidence = ruleResult.Confidence;
                isRuleBased = true;
                label = ruleResult.Label;
                subGrades = ruleResult.SubGrades;
            }

            estimates.Add(GradeEstimate.Create(
                cardSubmissionId, engine.Company, finalGrade, subGrades, confidence, isRuleBased, label));
        }

        return estimates;
    }
}
