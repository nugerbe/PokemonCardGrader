using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.DTOs;

/// <summary>
/// Output of the image analysis pipeline. Wraps the persistent
/// <see cref="ImageAnalysisResult"/> (scores, overlay, defects, confidence).
/// </summary>
/// <param name="Result">Scores, overlay, defects, confidence — the persistent record.</param>
public sealed record ImageAnalysisOutcome(
    ImageAnalysisResult Result);
