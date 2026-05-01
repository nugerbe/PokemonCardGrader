using PokemonCardGrader.Application.Interfaces;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// Default implementation of IMLModelRegistry that holds registered ML models.
/// Models can be registered at startup via DI configuration.
/// </summary>
public sealed class MLModelRegistry : IMLModelRegistry, IDisposable
{
    private readonly List<IMLModel> _models = [];

    /// <summary>Registers a model with the registry.</summary>
    public void Register(IMLModel model)
    {
        _models.Add(model);
    }

    public IReadOnlyList<IMLModel> GetAll() => _models.AsReadOnly();

    public IMLModel? GetByPurpose(MLModelPurpose purpose) =>
        _models.FirstOrDefault(m => m.Purpose == purpose && m.IsReady);

    public IMLModel? GetById(string modelId) =>
        _models.FirstOrDefault(m => m.ModelId == modelId);

    public void Dispose()
    {
        foreach (var model in _models)
            model.Dispose();
        _models.Clear();
    }
}
