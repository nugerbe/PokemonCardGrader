# PokemonCardGrader — Codemap

> Working reference for AI assistants. Updated 2026-05-01.

## Stack

- **.NET 10 / C# 13**, Blazor Server (Interactive SSR)
- **EF Core** + SQL Server (localdb), Identity v3 with Passkeys
- **OpenCvSharp** for image analysis, **ML.NET** + ONNX for ML grading
- **Channel\<T\>** for backpressure-safe image processing pipeline
- **Polly** for HTTP resilience (Pokemon TCG API)

## Solution Structure

```
PokemonCardGrader.slnx
src/
  PokemonCardGrader.Domain/          # Entities, ValueObjects, Enums, GradingEngines
  PokemonCardGrader.Application/     # DTOs, Interfaces, Services, Workers
  PokemonCardGrader.Infrastructure/  # EF Core, Repositories, ImageAnalysis, ML, Storage
  PokemonCardGrader.Web/             # Blazor pages, components, endpoints, Program.cs
tests/
  PokemonCardGrader.Domain.Tests/
  PokemonCardGrader.Application.Tests/
  PokemonCardGrader.Infrastructure.Tests/
  PokemonCardGrader.Web.Tests/
```

## Domain Entities

| Entity | Key Fields | Notes |
|--------|-----------|-------|
| `PokemonCard` | Name, SetName, SetCode, Number, Rarity, TcgApiId?, ImageUrl? | CreateFromApi / CreateManual |
| `CardSubmission` | UserId, PokemonCardId, ManualScores?, ImageDerivedScores?, FinalScores? | Owns Images[], Estimates[], ActualResult? |
| `CardImage` | CardSubmissionId, StoragePath, FileName, ImageType(Front/Back), FileSizeBytes, AnalysisResult? | Factory: Create() |
| `GradeEstimate` | CardSubmissionId, Company, PredictedGrade, SubGrades, Confidence, IsRuleBased, Label | Per-company predictions |
| `GradingResult` | CardSubmissionId, UserId, Company, ActualGrade, ActualSubGrades, CertificationNumber? | User-recorded actual grades |
| `AnalysisCorrection` | CardImageId, CardSubmissionId, OriginalOverlay, OriginalScores, Correction, CorrectedScores | ML training feedback data |

## Enums

- `ImageType`: Front=1, Back=2
- `GradingCompany`: PSA=1, BGS=2, CGC=3, ACE=4, SGC=5, TAG=6
- `CardRarity`: Common, Uncommon, Rare, RareHolo, RareUltra, RareSecret, RareRainbow, Promo, Other

## Key Value Objects

| VO | Fields |
|----|--------|
| `ConditionScores` | Centering, Corners(1-10), Edges(1-10), Surface(1-10) |
| `CenteringMeasurement` | LR/TB Front/Back (% from left/top), MaxDeviation, FrontRatio, BackRatio |
| `ImageAnalysisResult` | DetectedCentering, CornersScore, EdgesScore, SurfaceScore, DetectedDefects[], Overlay?, QualityAssessment?, FailureDetection?, Regions?, Features?, ML fields, Confidence fields |
| `DetectedDefect` | (defect description data) |
| `AnalysisOverlay` | (overlay visualization data) |
| `UserCorrection` | (user adjustments to analysis) |
| `CardFeatures` | Extracted feature vector for ML |
| `CardRegions` | Segmented card regions |
| `ConfidenceBreakdown` | Per-component confidence |

## Application Layer

### Interfaces (in Application/Interfaces/)
- `IImageStorageService` — Save/Get/Delete/GetUrl for images
- `IImageAnalysisService` — AnalyzeImageAsync, RecalculateFromCorrection
- `ICardSubmissionRepository` — CRUD for submissions
- `IGradingResultRepository` — CRUD for actual grades
- `IPokemonCardRepository` — CRUD for card catalog
- `IPokemonTcgApiClient` — External TCG API
- `IBorderPredictionService` — ML border prediction
- `IGradeEstimationService` — Grade estimation orchestration
- `IMlGradePredictor` — ML.NET model inference
- `IMlTrainingService` — ML.NET retraining
- `IMLModel` — ONNX model abstraction

### Services (in Application/Services/)
- `CardLookupService` — Pokemon TCG API search + local cache
- `CardSubmissionService` — Submission CRUD orchestration
- `ImageProcessingService` — Image upload + queue to Channel
- `DashboardService` — Dashboard aggregation

### Workers (in Application/Workers/)
- `ImageAnalysisWorker` — Background; reads Channel, runs analysis pipeline
- `MlRetrainingWorker` — Background; periodic ML model retraining

## Infrastructure Layer

### Data
- `ApplicationDbContext` : IdentityDbContext\<ApplicationUser\> — 6 DbSets
- `ApplicationUser` : IdentityUser (no extensions yet)
- Entity configurations in Data/Configurations/ (one per entity)
- Migrations: InitialCreate, AddIdentityV3Passkeys, AddRowVersion, RemoveRowVersion, AddAnalysisCorrections

### Image Analysis Pipeline (ImageAnalysis/)
CardDetector → CardNormalizer → AlignmentRefiner → RegionSegmenter →
CenteringAnalyzer + ConditionAnalyzer + AdvancedDefectAnalyzer →
FeatureExtractor → ConfidenceScorer → FailureDetector →
ImageQualityAnalyzer → FinalOutputAssembler → GradingReportBuilder

Orchestrator: `OpenCvImageAnalysisService` (implements IImageAnalysisService)

### ML (ML/)
- `MlNetGradePredictor` — ML.NET model load/predict
- `MlNetTrainingService` — Retraining from GradingResults
- `OnnxInferenceService` / `OnnxModel` — ONNX runtime inference
- `BorderPredictionService` — ML border detection
- `HybridScoreCombiner` — CV+ML score fusion
- `CalibrationService` / `ConfidenceCalibrator` — Score calibration
- `GradeEstimationService` — Orchestrates rule engines + ML
- `MLModelRegistry` — Model versioning/loading
- `UserFeedbackService` — Correction ingestion

### Storage
- `LocalImageStorageService` — wwwroot/uploads/{yyyy/MM/dd}/{guid}_{filename}

### Repositories
- `CardSubmissionRepository`, `GradingResultRepository`, `PokemonCardRepository`

## Web Layer

### Pages (Components/Pages/)
- `Home.razor` — Landing
- `NewSubmission.razor` — Upload + card selection
- `SubmissionDetail.razor` — View analysis results
- `SubmissionHistory.razor` — User's submissions list
- `GradedCards.razor` — Cards with actual grades recorded
- `CardSearch.razor` — Pokemon TCG API search
- `Dashboard.razor` — Analytics
- `MyCollection.razor` — User's card collection

### Shared Components (Components/Shared/)
- `ImageUploader.razor` — Drag-and-drop image upload
- `ImageOverlayEditor.razor` — Interactive defect/border overlay
- `ConditionScoreForm.razor` — Manual score entry
- `GradeEstimatePanel.razor` — Display predictions
- `GradeCompanyBadge.razor` — Company logo/badge
- `SubGradeBreakdown.razor` — Sub-grade display
- `RecordActualGradeForm.razor` — Record real grade

### Endpoints
- `GradingEndpoints.cs` — Minimal API endpoints for grading operations

## DI Registration Summary (Program.cs)

| Lifetime | Services |
|----------|----------|
| Singleton | GradingRuleEngineFactory, LocalImageStorageService, CardDetector, CardNormalizer, CenteringAnalyzer, ConditionAnalyzer, DebugVisualizer, AnalysisDataLogger, ConfidenceScorer, ImageQualityAnalyzer, FailureDetector, AlignmentRefiner, RegionSegmenter, AdvancedDefectAnalyzer, FeatureExtractor, MLModelRegistry, OnnxInferenceService, HybridScoreCombiner, CalibrationService, EvaluationService, ModelManager, UserFeedbackService, ConfidenceCalibrator, FinalOutputAssembler, MlNetGradePredictor, Channel\<ImageProcessingRequest\> |
| Scoped | Repositories (3), CardLookupService, CardSubmissionService, ImageProcessingService, DashboardService, BatchProcessor, BorderPredictionService, OpenCvImageAnalysisService, MlNetTrainingService, GradeEstimationService |
| Hosted | ImageAnalysisWorker, MlRetrainingWorker |

## Grading Rule Engines (Domain/GradingEngines/)
Factory pattern: `GradingRuleEngineFactory` → PSA, BGS, CGC, ACE, SGC, TAG engines
Each implements `IGradingRuleEngine` — maps ConditionScores → predicted grade for that company.

## Database
- SQL Server (localdb) — `PokemonCardGrader`
- Connection string in appsettings or hardcoded fallback
- 5 migrations applied as of 2026-04-21

## Key Patterns
- **DDD-lite**: Rich domain entities with private setters + factory methods
- **CQRS-light**: Repos for writes, services for reads
- **Channel pipeline**: ImageProcessingRequest queued → ImageAnalysisWorker dequeues + processes
- **Hybrid ML+CV**: OpenCV analysis merged with ONNX/ML.NET predictions via HybridScoreCombiner
