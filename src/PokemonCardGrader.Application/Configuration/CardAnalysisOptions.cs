namespace PokemonCardGrader.Application.Configuration;

/// <summary>
/// Configurable parameters for the card analysis pipeline.
/// Bind to "CardAnalysis" section in appsettings.json.
/// </summary>
public sealed class CardAnalysisOptions
{
    public const string SectionName = "CardAnalysis";

    // ── Card geometry ──
    /// <summary>Standard Pokemon card width in mm.</summary>
    public double CardWidthMm { get; set; } = 63.0;

    /// <summary>Standard Pokemon card height in mm.</summary>
    public double CardHeightMm { get; set; } = 88.0;

    /// <summary>Width of the normalized (perspective-corrected) card image in pixels.</summary>
    public int NormalizedWidth { get; set; } = 630;

    /// <summary>Height of the normalized card image in pixels.</summary>
    public int NormalizedHeight { get; set; } = 880;

    /// <summary>Derived aspect ratio (width / height).</summary>
    public double CardAspectRatio => CardWidthMm / CardHeightMm;

    // ── Card detection ──
    /// <summary>Minimum contour area as a fraction of the image for candidate quads.</summary>
    public double MinContourAreaFraction { get; set; } = 0.05;

    /// <summary>Maximum candidates per detection strategy.</summary>
    public int MaxCandidatesPerStrategy { get; set; } = 5;

    /// <summary>Canny threshold pairs for multi-threshold sweep (low, high).</summary>
    public int[][] CannyThresholds { get; set; } =
    [
        [30, 90],
        [50, 150],
        [80, 240]
    ];

    /// <summary>ApproxPolyDP epsilon fractions for polygon simplification cascade.</summary>
    public double[] ApproxEpsilons { get; set; } = [0.02, 0.04, 0.06];

    /// <summary>Full-frame fallback aspect ratio tolerance.</summary>
    public double FullFrameAspectTolerance { get; set; } = 0.08;

    /// <summary>Full-frame inset fraction from each edge.</summary>
    public double FullFrameInsetFraction { get; set; } = 0.02;

    // ── Candidate scoring weights ──
    public double ScoreWeightAspect { get; set; } = 0.35;
    public double ScoreWeightArea { get; set; } = 0.25;
    public double ScoreWeightConvexity { get; set; } = 0.15;
    public double ScoreWeightParallelism { get; set; } = 0.15;
    public double ScoreWeightConsistency { get; set; } = 0.10;

    /// <summary>Ideal area fraction range for candidate quads.</summary>
    public double IdealAreaMin { get; set; } = 0.30;
    public double IdealAreaMax { get; set; } = 0.80;

    // ── Border detection ──
    /// <summary>Minimum border position as a fraction of card dimension.</summary>
    public double MinBorderFraction { get; set; } = 0.02;

    /// <summary>Maximum border position as a fraction of card dimension.</summary>
    public double MaxBorderFraction { get; set; } = 0.16;

    /// <summary>Minimum edge density to qualify as a border candidate.</summary>
    public double MinEdgeDensity { get; set; } = 0.12;

    /// <summary>Default border fallback fraction when detection fails and no prior exists.</summary>
    public double DefaultBorderFallback { get; set; } = 0.05;

    /// <summary>Canny thresholds for centering border detection (low, high).</summary>
    public int[][] CenteringCannyThresholds { get; set; } =
    [
        [40, 120],
        [80, 200],
        [120, 280]
    ];

    /// <summary>Maximum prior blending weight (scales with prior confidence).</summary>
    public double MaxPriorBlendWeight { get; set; } = 0.25;

    // ── Surface analysis ──
    /// <summary>CLAHE clip limit for surface enhancement.</summary>
    public double ClaheClipLimit { get; set; } = 2.0;

    /// <summary>CLAHE tile grid size (square).</summary>
    public int ClaheTileSize { get; set; } = 8;

    /// <summary>Grid rows for surface cell variance analysis.</summary>
    public int SurfaceGridRows { get; set; } = 5;

    /// <summary>Grid columns for surface cell variance analysis.</summary>
    public int SurfaceGridCols { get; set; } = 4;

    /// <summary>Multiplier of median variance to classify a cell as an outlier.</summary>
    public double SurfaceOutlierMultiplier { get; set; } = 2.5;

    /// <summary>Border fraction to exclude when creating the inner ROI for surface analysis.</summary>
    public double SurfaceBorderExclude { get; set; } = 0.10;

    /// <summary>Minimum sharpness (Laplacian stddev) below which a penalty is applied.</summary>
    public double MinSharpness { get; set; } = 5.0;

    // ── Corner analysis ──
    /// <summary>Corner region size as a fraction of normalized card width.</summary>
    public double CornerSizeFraction { get; set; } = 0.08;

    /// <summary>Threshold for whitening detection in corners (0-255).</summary>
    public double CornerWhiteThreshold { get; set; } = 220;

    /// <summary>White pixel ratio above which whitening penalty applies.</summary>
    public double CornerWhiteRatioThreshold { get; set; } = 0.3;

    /// <summary>Minimum sharpness in corner region (Laplacian stddev).</summary>
    public double CornerMinSharpness { get; set; } = 10.0;

    /// <summary>Weight of worst corner vs average in final score.</summary>
    public double CornerWorstWeight { get; set; } = 0.6;

    // ── Edge analysis ──
    /// <summary>Edge strip inset from card edge as a fraction of card width.</summary>
    public double EdgeInsetFraction { get; set; } = 0.01;

    /// <summary>Edge strip width as a fraction of card width.</summary>
    public double EdgeWidthFraction { get; set; } = 0.03;

    /// <summary>Threshold for whitening on dark borders (mean < 180).</summary>
    public double EdgeWhiteThresholdDark { get; set; } = 245;

    /// <summary>Threshold for whitening on light borders (mean >= 180).</summary>
    public double EdgeWhiteThresholdLight { get; set; } = 252;

    /// <summary>White ratio above which whitening penalty applies.</summary>
    public double EdgeWhiteRatioThreshold { get; set; } = 0.25;

    /// <summary>Gradient magnitude above which damage penalty applies.</summary>
    public double EdgeGradientThreshold { get; set; } = 80.0;

    /// <summary>Weight of worst edge vs average in final score.</summary>
    public double EdgeWorstWeight { get; set; } = 0.4;

    // ── Defect detection ──
    /// <summary>Morphological gradient thresholds for cross-validation.</summary>
    public int[] DefectThresholds { get; set; } = [30, 40, 55];

    /// <summary>Minimum contour area in pixels to consider as a defect.</summary>
    public double DefectMinArea { get; set; } = 50;

    /// <summary>Maximum defect area as a fraction of the normalized card.</summary>
    public double DefectMaxAreaFraction { get; set; } = 0.05;

    /// <summary>Aspect ratio threshold for scratch classification.</summary>
    public double ScratchAspectThreshold { get; set; } = 4.0;

    /// <summary>Minimum area in pixels for dent classification.</summary>
    public double DentMinArea { get; set; } = 500;

    /// <summary>Maximum defects to return.</summary>
    public int MaxDefects { get; set; } = 10;

    /// <summary>Minimum confidence floor for detected defects.</summary>
    public double DefectMinConfidence { get; set; } = 0.15;

    /// <summary>Maximum confidence ceiling for CV-only defect detection.</summary>
    public double DefectMaxConfidence { get; set; } = 0.95;

    // ── ML Inference ──
    /// <summary>Width to resize card image for ML model input.</summary>
    public int MlInputWidth { get; set; } = 224;

    /// <summary>Height to resize card image for ML model input.</summary>
    public int MlInputHeight { get; set; } = 224;

    /// <summary>Minimum confidence threshold for ML-detected defects.</summary>
    public double MlDefectConfidenceThreshold { get; set; } = 0.3;

    /// <summary>Maximum number of ML defect detections to return.</summary>
    public int MlMaxDefects { get; set; } = 20;

    // ── Confidence Scoring ──
    /// <summary>Weight of image quality in overall confidence.</summary>
    public double ConfidenceWeightImageQuality { get; set; } = 0.25;

    /// <summary>Weight of detection reliability in overall confidence.</summary>
    public double ConfidenceWeightDetection { get; set; } = 0.30;

    /// <summary>Weight of CV/ML agreement in overall confidence.</summary>
    public double ConfidenceWeightCvMlAgreement { get; set; } = 0.20;

    /// <summary>Weight of border consistency in overall confidence.</summary>
    public double ConfidenceWeightBorderConsistency { get; set; } = 0.25;

    /// <summary>Overall confidence below this threshold triggers low-confidence warning.</summary>
    public double LowConfidenceThreshold { get; set; } = 0.5;

    /// <summary>Minimum Laplacian stddev for the image to be considered sharp enough.</summary>
    public double MinSharpnessForQuality { get; set; } = 15.0;

    /// <summary>Ideal mean brightness lower bound (0-255).</summary>
    public double IdealBrightnessMin { get; set; } = 60.0;

    /// <summary>Ideal mean brightness upper bound (0-255).</summary>
    public double IdealBrightnessMax { get; set; } = 200.0;

    // ── Continuous Learning ──
    /// <summary>Enable logging of analysis data for ML retraining regardless of debug mode.</summary>
    public bool ContinuousLearningEnabled { get; set; } = true;

    /// <summary>Directory for continuous learning data output.</summary>
    public string LearningDataPath { get; set; } = "learning-data";

    // ── Image Quality Gate (Phase 10) ──
    /// <summary>Minimum overall quality score to proceed with grading (0-1).</summary>
    public double QualityGateThreshold { get; set; } = 0.3;

    /// <summary>Quality score below which confidence is reduced instead of rejecting.</summary>
    public double QualityReduceConfidenceThreshold { get; set; } = 0.5;

    /// <summary>Minimum image dimension (width or height) in pixels for adequate resolution.</summary>
    public int MinImageDimension { get; set; } = 300;

    /// <summary>Ideal minimum image dimension for high-quality analysis.</summary>
    public int IdealImageDimension { get; set; } = 800;

    /// <summary>Maximum fraction of saturated (near-white) pixels for glare detection.</summary>
    public double MaxGlareFraction { get; set; } = 0.15;

    /// <summary>Saturation threshold (0-255) above which a pixel is considered glare.</summary>
    public int GlareSaturationThreshold { get; set; } = 250;

    /// <summary>Maximum acceptable noise level (estimated via median filter deviation).</summary>
    public double MaxNoiseLevel { get; set; } = 25.0;

    // ── Alignment Refinement (Phase 11) ──
    /// <summary>Maximum rotation correction in degrees during alignment refinement.</summary>
    public double MaxAlignmentRotation { get; set; } = 3.0;

    /// <summary>Margin fraction to crop after alignment (removes edge artifacts).</summary>
    public double AlignmentCropMargin { get; set; } = 0.005;

    // ── Region Segmentation (Phase 12) ──
    /// <summary>Border region width as a fraction of card dimension for segmentation.</summary>
    public double SegmentBorderFraction { get; set; } = 0.06;

    /// <summary>Artwork region top offset as a fraction of card height.</summary>
    public double ArtworkTopFraction { get; set; } = 0.08;

    /// <summary>Artwork region height as a fraction of card height.</summary>
    public double ArtworkHeightFraction { get; set; } = 0.48;

    /// <summary>Text region top offset as a fraction of card height.</summary>
    public double TextTopFraction { get; set; } = 0.58;

    /// <summary>Text region height as a fraction of card height.</summary>
    public double TextHeightFraction { get; set; } = 0.35;

    /// <summary>Corner zone size as a fraction of card width for segmentation.</summary>
    public double SegmentCornerFraction { get; set; } = 0.10;

    /// <summary>Edge zone width as a fraction of card dimension for segmentation.</summary>
    public double SegmentEdgeFraction { get; set; } = 0.04;

    // ── Advanced Defect Analysis (Phase 13) ──
    /// <summary>Number of sample points along each edge for continuity scoring.</summary>
    public int EdgeContinuitySamples { get; set; } = 50;

    /// <summary>Gradient threshold for edge chipping detection.</summary>
    public double ChipGradientThreshold { get; set; } = 100.0;

    /// <summary>Expected corner radius in pixels (on normalized image).</summary>
    public double ExpectedCornerRadius { get; set; } = 12.0;

    /// <summary>Maximum deviation from expected corner radius before penalty.</summary>
    public double CornerRadiusTolerance { get; set; } = 4.0;

    /// <summary>Gabor filter frequency for scratch/print line detection.</summary>
    public double TextureGaborFrequency { get; set; } = 0.15;

    /// <summary>Number of Gabor orientations for texture analysis.</summary>
    public int TextureGaborOrientations { get; set; } = 8;

    /// <summary>Color deviation threshold for whitening detection (in Lab color space).</summary>
    public double ColorDeviationThreshold { get; set; } = 15.0;

    // ── Feature Extraction (Phase 14) ──
    /// <summary>Number of histogram bins per HSV channel for color features.</summary>
    public int ColorHistogramBins { get; set; } = 16;

    /// <summary>Grid size for surface variance feature extraction.</summary>
    public int FeatureGridSize { get; set; } = 4;

    // ── Hybrid ML Strategy (Phase 15) ──
    /// <summary>Weight of CV scores in hybrid combination when ML is available.</summary>
    public double HybridCvWeight { get; set; } = 0.4;

    /// <summary>Weight of ML scores in hybrid combination.</summary>
    public double HybridMlWeight { get; set; } = 0.6;

    /// <summary>Minimum ML model confidence to use ML results.</summary>
    public double HybridMinMlConfidence { get; set; } = 0.5;

    // ── Calibration (Phase 16) ──
    /// <summary>Directory for calibration data persistence.</summary>
    public string CalibrationDataPath { get; set; } = "calibration-data";

    /// <summary>Minimum records required before applying calibration corrections.</summary>
    public int CalibrationMinSamples { get; set; } = 20;

    /// <summary>Maximum bias correction magnitude to apply.</summary>
    public double CalibrationMaxBiasCorrection { get; set; } = 1.0;

    // ── Evaluation (Phase 17) ──
    /// <summary>Directory for evaluation metrics persistence.</summary>
    public string EvaluationDataPath { get; set; } = "evaluation-data";

    // ── Model Versioning (Phase 18) ──
    /// <summary>Directory for model version metadata.</summary>
    public string ModelVersionPath { get; set; } = "model-versions";

    // ── Failure Handling (Phase 21) ──
    /// <summary>Maximum number of card-like objects before triggering multi-card failure.</summary>
    public int MaxAllowedCards { get; set; } = 1;

    /// <summary>Minimum visible card area fraction to proceed with grading.</summary>
    public double MinVisibleAreaFraction { get; set; } = 0.85;

    /// <summary>Reflectance ratio threshold for sleeve/top-loader detection.</summary>
    public double SleeveReflectanceThreshold { get; set; } = 0.4;

    /// <summary>Edge density threshold for secondary card detection.</summary>
    public double SecondaryCardEdgeDensity { get; set; } = 0.15;

    // ── Performance (Phase 22) ──
    /// <summary>Maximum number of images to process in a single batch.</summary>
    public int BatchSize { get; set; } = 4;

    /// <summary>Maximum degree of parallelism for batch processing.</summary>
    public int MaxParallelism { get; set; } = 2;

    // ── Debug ──
    /// <summary>Enable debug image output (contours, edges, overlays).</summary>
    public bool DebugEnabled { get; set; }

    /// <summary>Directory for debug image output.</summary>
    public string DebugOutputPath { get; set; } = "debug-output";
}
