namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Extracted feature vector from a normalized card image.
/// Used as input for ML models and hybrid scoring.
/// </summary>
public sealed record CardFeatures
{
    /// <summary>Edge roughness per side [Top, Right, Bottom, Left]. Higher = rougher edges.</summary>
    public required double[] EdgeRoughness { get; init; }

    /// <summary>Corner geometry metrics [TL, TR, BR, BL]. Curvature radius deviation from ideal.</summary>
    public required double[] CornerGeometry { get; init; }

    /// <summary>Surface variance across grid cells. Higher = more surface inconsistency.</summary>
    public required double[] SurfaceVariance { get; init; }

    /// <summary>Surface texture energy per grid cell (Gabor filter response).</summary>
    public required double[] SurfaceTexture { get; init; }

    /// <summary>Color histogram features (HSV, 16 bins per channel = 48 values).</summary>
    public required double[] ColorHistogram { get; init; }

    /// <summary>Border thickness measurements per side [Top, Right, Bottom, Left] in fraction of card dimension.</summary>
    public required double[] BorderThickness { get; init; }

    /// <summary>Overall sharpness (Laplacian variance of the full normalized image).</summary>
    public required double Sharpness { get; init; }

    /// <summary>Centering deviation from 50/50 [LR, TB] as percentages.</summary>
    public required double[] CenteringDeviation { get; init; }

    /// <summary>Whitening ratio per corner [TL, TR, BR, BL].</summary>
    public required double[] CornerWhitening { get; init; }

    /// <summary>Whitening ratio per edge [Top, Right, Bottom, Left].</summary>
    public required double[] EdgeWhitening { get; init; }

    /// <summary>Converts the entire feature set to a flat array for ML input.</summary>
    public double[] ToFlatArray()
    {
        var result = new List<double>();
        result.AddRange(EdgeRoughness);
        result.AddRange(CornerGeometry);
        result.AddRange(SurfaceVariance);
        result.AddRange(SurfaceTexture);
        result.AddRange(ColorHistogram);
        result.AddRange(BorderThickness);
        result.Add(Sharpness);
        result.AddRange(CenteringDeviation);
        result.AddRange(CornerWhitening);
        result.AddRange(EdgeWhitening);
        return result.ToArray();
    }
}
