using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;

namespace PokemonCardGrader.Application.Services;

public sealed class DashboardService(
    ICardSubmissionRepository submissionRepository,
    CardSubmissionService submissionService)
{
    public async Task<DashboardDto> GetDashboardAsync(string userId, CancellationToken ct = default)
    {
        var totalSubmissions = await submissionRepository.GetCountByUserIdAsync(userId, ct);
        var recentSubmissions = await submissionRepository.GetRecentByUserIdAsync(userId, 5, ct);

        var gradeDistribution = new Dictionary<string, int>();
        var gradedCount = 0;
        double gradeSum = 0;
        var gradeCount = 0;

        foreach (var submission in recentSubmissions)
        {
            if (submission.ActualResult is not null)
                gradedCount++;

            foreach (var estimate in submission.Estimates)
            {
                if (estimate.Company == Domain.Enums.GradingCompany.PSA)
                {
                    gradeSum += estimate.PredictedGrade;
                    gradeCount++;

                    var bucket = estimate.PredictedGrade switch
                    {
                        >= 9.5 => "10",
                        >= 8.5 => "9",
                        >= 7.5 => "8",
                        >= 6.5 => "7",
                        _ => "6 or below"
                    };

                    gradeDistribution[bucket] = gradeDistribution.GetValueOrDefault(bucket) + 1;
                }
            }
        }

        return new DashboardDto
        {
            TotalSubmissions = totalSubmissions,
            GradedCards = gradedCount,
            GradeDistribution = gradeDistribution,
            RecentSubmissions = recentSubmissions.Select(submissionService.MapToDto).ToList(),
            AverageEstimatedGrade = gradeCount > 0 ? gradeSum / gradeCount : null
        };
    }
}
