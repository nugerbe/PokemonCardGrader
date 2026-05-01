using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Domain.GradingEngines;

public sealed class GradingRuleEngineFactory
{
    private readonly Dictionary<GradingCompany, IGradingRuleEngine> _engines;

    public GradingRuleEngineFactory()
    {
        var engines = new IGradingRuleEngine[]
        {
            new PsaGradingEngine(),
            new BgsGradingEngine(),
            new CgcGradingEngine(),
            new AceGradingEngine(),
            new SgcGradingEngine(),
            new TagGradingEngine()
        };

        _engines = engines.ToDictionary(e => e.Company);
    }

    public IGradingRuleEngine GetEngine(GradingCompany company)
    {
        return _engines.TryGetValue(company, out var engine)
            ? engine
            : throw new ArgumentException($"No grading engine registered for {company}", nameof(company));
    }

    public IReadOnlyList<IGradingRuleEngine> GetAllEngines() => [.. _engines.Values];
}
