using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// Phase 18: Manages ML model versions — stores metadata, supports switching
/// between versions, and enables rollback to previous model versions.
/// </summary>
public sealed class ModelManager(
    MLModelRegistry registry,
    IOptions<CardAnalysisOptions> options,
    ILogger<ModelManager> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;
    private readonly Lock _lock = new();
    private List<ModelVersionInfo>? _versions;

    /// <summary>
    /// Registers a new model version with metadata.
    /// </summary>
    public ModelVersionInfo RegisterVersion(
        string modelId, string version, string purpose,
        int trainingSampleCount, string modelPath,
        Dictionary<string, double>? performanceMetrics = null,
        string? datasetHash = null, string? notes = null)
    {
        var info = new ModelVersionInfo
        {
            ModelId = modelId,
            Version = version,
            Purpose = purpose,
            TrainedAt = DateTimeOffset.UtcNow,
            RegisteredAt = DateTimeOffset.UtcNow,
            TrainingSampleCount = trainingSampleCount,
            TrainingDatasetHash = datasetHash,
            PerformanceMetrics = performanceMetrics ?? new Dictionary<string, double>(),
            IsActive = false,
            ModelPath = modelPath,
            Notes = notes
        };

        lock (_lock)
        {
            var versions = LoadVersions();
            versions.Add(info);
            SaveVersions(versions);
        }

        logger.LogInformation(
            "Model version registered: {ModelId} v{Version} purpose={Purpose} samples={Samples}",
            modelId, version, purpose, trainingSampleCount);

        return info;
    }

    /// <summary>
    /// Activates a specific model version, deactivating all other versions of the same purpose.
    /// </summary>
    public bool ActivateVersion(string modelId, string version)
    {
        lock (_lock)
        {
            var versions = LoadVersions();
            var target = versions.FirstOrDefault(v => v.ModelId == modelId && v.Version == version);

            if (target is null)
            {
                logger.LogWarning("Model version not found: {ModelId} v{Version}", modelId, version);
                return false;
            }

            // Deactivate all versions with the same purpose
            for (var i = 0; i < versions.Count; i++)
            {
                if (versions[i].Purpose == target.Purpose && versions[i].IsActive)
                {
                    versions[i] = versions[i] with { IsActive = false };
                }
            }

            // Activate the target
            var idx = versions.IndexOf(target);
            versions[idx] = target with { IsActive = true };

            SaveVersions(versions);
        }

        logger.LogInformation("Activated model version: {ModelId} v{Version}", modelId, version);
        return true;
    }

    /// <summary>
    /// Rolls back to the previous active version of a model.
    /// </summary>
    public bool RollbackToPrevious(string modelId)
    {
        lock (_lock)
        {
            var versions = LoadVersions();
            var modelVersions = versions
                .Where(v => v.ModelId == modelId)
                .OrderByDescending(v => v.RegisteredAt)
                .ToList();

            if (modelVersions.Count < 2)
            {
                logger.LogWarning("Cannot rollback {ModelId}: fewer than 2 versions available.", modelId);
                return false;
            }

            var current = modelVersions.FirstOrDefault(v => v.IsActive);
            var previous = current is not null
                ? modelVersions.FirstOrDefault(v => !v.IsActive)
                : modelVersions.First();

            if (previous is null)
            {
                logger.LogWarning("Cannot find previous version for {ModelId}.", modelId);
                return false;
            }

            // Deactivate current
            if (current is not null)
            {
                var currentIdx = versions.IndexOf(current);
                versions[currentIdx] = current with { IsActive = false };
            }

            // Activate previous
            var prevIdx = versions.IndexOf(previous);
            versions[prevIdx] = previous with { IsActive = true };

            SaveVersions(versions);

            logger.LogInformation(
                "Rolled back {ModelId} from v{CurrentVersion} to v{PreviousVersion}",
                modelId,
                current?.Version ?? "none",
                previous.Version);
        }

        return true;
    }

    /// <summary>
    /// Returns all registered versions for a given model.
    /// </summary>
    public IReadOnlyList<ModelVersionInfo> GetVersions(string? modelId = null)
    {
        lock (_lock)
        {
            var versions = LoadVersions();
            if (modelId is not null)
            {
                return versions.Where(v => v.ModelId == modelId).ToList().AsReadOnly();
            }
            return versions.AsReadOnly();
        }
    }

    /// <summary>
    /// Returns the currently active version for a given model, or null.
    /// </summary>
    public ModelVersionInfo? GetActiveVersion(string modelId)
    {
        lock (_lock)
        {
            var versions = LoadVersions();
            return versions.FirstOrDefault(v => v.ModelId == modelId && v.IsActive);
        }
    }

    /// <summary>
    /// Returns version info for all currently active models.
    /// </summary>
    public IReadOnlyList<ModelVersionInfo> GetAllActiveVersions()
    {
        lock (_lock)
        {
            var versions = LoadVersions();
            return versions.Where(v => v.IsActive).ToList().AsReadOnly();
        }
    }

    private List<ModelVersionInfo> LoadVersions()
    {
        if (_versions is not null) return _versions;

        var path = GetDataPath();
        if (!File.Exists(path))
        {
            _versions = [];
            return _versions;
        }

        try
        {
            var json = File.ReadAllText(path);
            _versions = JsonSerializer.Deserialize<List<ModelVersionInfo>>(json) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load model versions from {Path}.", path);
            _versions = [];
        }

        return _versions;
    }

    private void SaveVersions(List<ModelVersionInfo> versions)
    {
        _versions = versions;
        var path = GetDataPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            var json = JsonSerializer.Serialize(versions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save model versions to {Path}.", path);
        }
    }

    private string GetDataPath() =>
        Path.Combine(_opts.ModelVersionPath, "model-versions.json");
}
