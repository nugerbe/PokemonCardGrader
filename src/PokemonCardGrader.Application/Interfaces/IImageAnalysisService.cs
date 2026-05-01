using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.Interfaces;

public interface IImageAnalysisService
{
    /// <summary>
    /// Runs the full analysis pipeline and returns both the persistent
    /// <see cref="ImageAnalysisResult"/> and any transient artifacts (the
    /// rectified card JPEG) bundled in a single <see cref="ImageAnalysisOutcome"/>.
    /// </summary>
    Task<ImageAnalysisOutcome> AnalyzeImageAsync(Stream imageStream, ImageType imageType = ImageType.Front, CancellationToken ct = default);

    /// <summary>
    /// Recalculates scores from user corrections without re-running full image analysis.
    /// Adjusted borders → recalculate centering. Dismissed defects → improve edges/surface scores.
    /// </summary>
    ImageAnalysisResult RecalculateFromCorrection(ImageAnalysisResult original, UserCorrection correction);
}
