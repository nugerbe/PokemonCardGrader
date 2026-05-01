using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.DTOs;

/// <summary>
/// Output of the image analysis pipeline. Bundles the persistent
/// <see cref="ImageAnalysisResult"/> with transient artifacts produced during
/// analysis — currently the rectified card JPEG, which the worker writes to
/// storage but is not part of the result that gets serialized into the
/// database. Keeping the bytes off <see cref="ImageAnalysisResult"/> avoids
/// bloating the JSON column on every save and read.
/// </summary>
/// <param name="Result">Scores, overlay, defects, confidence — the persistent record.</param>
/// <param name="RectifiedImageJpeg">
/// JPEG bytes of the perspective-corrected card, or null if the pipeline
/// could not produce one (e.g. card detection failed). Persist via
/// <c>IImageStorageService</c> and record the path on
/// <c>CardImage.NormalizedStoragePath</c>.
/// </param>
public sealed record ImageAnalysisOutcome(
    ImageAnalysisResult Result,
    byte[]? RectifiedImageJpeg);
