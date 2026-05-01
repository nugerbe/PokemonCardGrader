using System.Threading.Channels;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Application.Services;
using PokemonCardGrader.Application.Workers;
using PokemonCardGrader.Domain.GradingEngines;
using PokemonCardGrader.Infrastructure.Data;
using PokemonCardGrader.Infrastructure.ExternalApis;
using PokemonCardGrader.Infrastructure.ImageAnalysis;
using PokemonCardGrader.Infrastructure.ML;
using PokemonCardGrader.Infrastructure.Repositories;
using PokemonCardGrader.Infrastructure.Storage;
using PokemonCardGrader.Web.Components;
using PokemonCardGrader.Web.Components.Account;
using PokemonCardGrader.Web.Endpoints;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Authentication & Identity
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// EF Core + SQL Server
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\mssqllocaldb;Database=PokemonCardGrader;Trusted_Connection=True;MultipleActiveResultSets=true";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure();
        sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
    })
    .EnableSensitiveDataLogging(false)
    .LogTo(_ => { }, LogLevel.Warning));

// Suppress verbose EF Core query logging
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.Warning);

// Suppress warnings for runtime-uploaded files not in MapStaticAssets' build-time manifest.
// Uploaded images in wwwroot/uploads/ are served by UseStaticFiles() middleware instead.
builder.Logging.AddFilter("Microsoft.AspNetCore.Builder.StaticAssetDevelopmentRuntimeHandler", LogLevel.Error);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Domain
builder.Services.AddSingleton<GradingRuleEngineFactory>();

// Repositories
builder.Services.AddScoped<IPokemonCardRepository, PokemonCardRepository>();
builder.Services.AddScoped<ICardSubmissionRepository, CardSubmissionRepository>();
builder.Services.AddScoped<IGradingResultRepository, GradingResultRepository>();

// Application Services
builder.Services.AddScoped<CardLookupService>();
builder.Services.AddScoped<CardSubmissionService>();
builder.Services.AddScoped<ImageProcessingService>();
builder.Services.AddScoped<DashboardService>();

// Pokemon TCG API client with Polly resilience
builder.Services.AddHttpClient("PokemonTcgApi", client =>
{
    client.BaseAddress = new Uri("https://api.pokemontcg.io/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");

    var apiKey = builder.Configuration["PokemonTcgApi:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
})
.AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));

builder.Services.AddScoped<IPokemonTcgApiClient, PokemonTcgApiClientAdapter>();

// Image Storage
builder.Services.AddSingleton<IImageStorageService>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new LocalImageStorageService(env.WebRootPath);
});

// Image Analysis – configuration
builder.Services.Configure<CardAnalysisOptions>(
    builder.Configuration.GetSection(CardAnalysisOptions.SectionName));

// Image Analysis – modular components (stateless, singleton-safe)
builder.Services.AddSingleton<CardDetector>();
builder.Services.AddSingleton<CardNormalizer>();
builder.Services.AddSingleton<CenteringAnalyzer>();
builder.Services.AddSingleton<ConditionAnalyzer>();
builder.Services.AddSingleton<DebugVisualizer>();
builder.Services.AddSingleton<AnalysisDataLogger>();
builder.Services.AddSingleton<ConfidenceScorer>();

// Image Analysis – Phase 10-14, 21: CV analysis pipeline components
builder.Services.AddSingleton<ImageQualityAnalyzer>();
builder.Services.AddSingleton<FailureDetector>();
builder.Services.AddSingleton<AlignmentRefiner>();
builder.Services.AddSingleton<RegionSegmenter>();
builder.Services.AddSingleton<AdvancedDefectAnalyzer>();
builder.Services.AddSingleton<FeatureExtractor>();

// Image Analysis – ML model registry and ONNX inference
builder.Services.AddSingleton<MLModelRegistry>();
builder.Services.AddSingleton<IMLModelRegistry>(sp => sp.GetRequiredService<MLModelRegistry>());
builder.Services.AddSingleton<OnnxInferenceService>();

// Image Analysis – Phase 15-20: ML/calibration pipeline components
builder.Services.AddSingleton<HybridScoreCombiner>();
builder.Services.AddSingleton<CalibrationService>();
builder.Services.AddSingleton<EvaluationService>();
builder.Services.AddSingleton<ModelManager>();
builder.Services.AddSingleton<UserFeedbackService>();
builder.Services.AddSingleton<ConfidenceCalibrator>();

// Image Analysis – Phase 22, 24: batch processing and output assembly
builder.Services.AddScoped<BatchProcessor>();
builder.Services.AddSingleton<FinalOutputAssembler>();

// Image Analysis – orchestrator
builder.Services.AddScoped<IBorderPredictionService, BorderPredictionService>();
builder.Services.AddScoped<IImageAnalysisService, OpenCvImageAnalysisService>();

// ML Pipeline
var modelsPath = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "models");
builder.Services.AddSingleton(sp =>
    new MlNetGradePredictor(modelsPath, sp.GetRequiredService<ILogger<MlNetGradePredictor>>()));
builder.Services.AddSingleton<IMlGradePredictor>(sp => sp.GetRequiredService<MlNetGradePredictor>());
builder.Services.AddScoped<IMlTrainingService, MlNetTrainingService>(sp =>
    new MlNetTrainingService(
        modelsPath,
        sp.GetRequiredService<IGradingResultRepository>(),
        sp.GetRequiredService<MlNetGradePredictor>(),
        sp.GetRequiredService<ILogger<MlNetTrainingService>>()));
builder.Services.AddScoped<IGradeEstimationService, GradeEstimationService>();

// Channel for image processing pipeline
builder.Services.AddSingleton(Channel.CreateBounded<ImageProcessingRequest>(
    new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true
    }));

// Background Workers
builder.Services.AddHostedService<ImageAnalysisWorker>();
builder.Services.AddHostedService<MlRetrainingWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

// Phase 23: Grading API endpoints
app.MapGradingEndpoints();

app.Run();
