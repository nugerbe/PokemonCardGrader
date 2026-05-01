namespace PokemonCardGrader.Domain.Enums;

/// <summary>
/// Origin of an <see cref="Entities.ImageAnalysisRecord"/>: was it produced by
/// the analysis pipeline running against a fresh upload, or computed from a
/// user correction layered on top of a previous analysis?
/// </summary>
public enum AnalysisRecordSource
{
    /// <summary>Initial run of the analysis pipeline against the uploaded image.</summary>
    Initial = 0,

    /// <summary>Recalculation triggered by user correction (border drag, dismissed defect, etc.).</summary>
    UserCorrection = 1
}
