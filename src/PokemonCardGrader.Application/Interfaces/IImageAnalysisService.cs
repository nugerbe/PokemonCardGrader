using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.Interfaces;

public interface IImageAnalysisService
{
    Task<ImageAnalysisResult> AnalyzeImageAsync(Stream imageStream, ImageType imageType = ImageType.Front, CancellationToken ct = default);

    /// <summary>
    /// Recalculates scores from user corrections without re-running full image analysis.
    /// Adjusted borders → recalculate centering. Dismissed defects → improve edges/surface scores.
    /// </summary>
    ImageAnalysisResult RecalculateFromCorrection(ImageAnalysisResult original, UserCorrection correction);
}
