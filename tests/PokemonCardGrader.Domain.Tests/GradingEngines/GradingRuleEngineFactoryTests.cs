using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.GradingEngines;

namespace PokemonCardGrader.Domain.Tests.GradingEngines;

public class GradingRuleEngineFactoryTests
{
    private readonly GradingRuleEngineFactory _factory = new();

    [Fact]
    public void GetEngine_ForPSA_ReturnsPsaGradingEngine()
    {
        // Act
        var engine = _factory.GetEngine(GradingCompany.PSA);

        // Assert
        Assert.NotNull(engine);
        Assert.IsType<PsaGradingEngine>(engine);
        Assert.Equal(GradingCompany.PSA, engine.Company);
    }

    [Fact]
    public void GetEngine_ForBGS_ReturnsBgsGradingEngine()
    {
        // Act
        var engine = _factory.GetEngine(GradingCompany.BGS);

        // Assert
        Assert.NotNull(engine);
        Assert.IsType<BgsGradingEngine>(engine);
        Assert.Equal(GradingCompany.BGS, engine.Company);
    }

    [Fact]
    public void GetEngine_ForCGC_ReturnsCgcGradingEngine()
    {
        // Act
        var engine = _factory.GetEngine(GradingCompany.CGC);

        // Assert
        Assert.NotNull(engine);
        Assert.IsType<CgcGradingEngine>(engine);
        Assert.Equal(GradingCompany.CGC, engine.Company);
    }

    [Fact]
    public void GetEngine_ForACE_ReturnsAceGradingEngine()
    {
        // Act
        var engine = _factory.GetEngine(GradingCompany.ACE);

        // Assert
        Assert.NotNull(engine);
        Assert.IsType<AceGradingEngine>(engine);
        Assert.Equal(GradingCompany.ACE, engine.Company);
    }

    [Fact]
    public void GetEngine_ForSGC_ReturnsSgcGradingEngine()
    {
        // Act
        var engine = _factory.GetEngine(GradingCompany.SGC);

        // Assert
        Assert.NotNull(engine);
        Assert.IsType<SgcGradingEngine>(engine);
        Assert.Equal(GradingCompany.SGC, engine.Company);
    }

    [Fact]
    public void GetEngine_ForTAG_ReturnsTagGradingEngine()
    {
        // Act
        var engine = _factory.GetEngine(GradingCompany.TAG);

        // Assert
        Assert.NotNull(engine);
        Assert.IsType<TagGradingEngine>(engine);
        Assert.Equal(GradingCompany.TAG, engine.Company);
    }

    [Theory]
    [InlineData(GradingCompany.PSA)]
    [InlineData(GradingCompany.BGS)]
    [InlineData(GradingCompany.CGC)]
    [InlineData(GradingCompany.ACE)]
    [InlineData(GradingCompany.SGC)]
    [InlineData(GradingCompany.TAG)]
    public void GetEngine_ForAllCompanies_ReturnsEngine(GradingCompany company)
    {
        // Act
        var engine = _factory.GetEngine(company);

        // Assert
        Assert.NotNull(engine);
        Assert.Equal(company, engine.Company);
    }

    [Fact]
    public void GetEngine_WithInvalidCompany_ThrowsArgumentException()
    {
        // Arrange
        var invalidCompany = (GradingCompany)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _factory.GetEngine(invalidCompany));
        Assert.Contains("No grading engine registered for", exception.Message);
        Assert.Equal("company", exception.ParamName);
    }

    [Fact]
    public void GetAllEngines_ReturnsAllSixEngines()
    {
        // Act
        var engines = _factory.GetAllEngines();

        // Assert
        Assert.NotNull(engines);
        Assert.Equal(6, engines.Count);
    }

    [Fact]
    public void GetAllEngines_ContainsAllCompanies()
    {
        // Act
        var engines = _factory.GetAllEngines();
        var companies = engines.Select(e => e.Company).ToList();

        // Assert
        Assert.Contains(GradingCompany.PSA, companies);
        Assert.Contains(GradingCompany.BGS, companies);
        Assert.Contains(GradingCompany.CGC, companies);
        Assert.Contains(GradingCompany.ACE, companies);
        Assert.Contains(GradingCompany.SGC, companies);
        Assert.Contains(GradingCompany.TAG, companies);
    }

    [Fact]
    public void GetAllEngines_ReturnsUniqueCompanies()
    {
        // Act
        var engines = _factory.GetAllEngines();
        var companies = engines.Select(e => e.Company).ToList();

        // Assert
        Assert.Equal(companies.Count, companies.Distinct().Count());
    }

    [Fact]
    public void GetAllEngines_ReturnsReadOnlyList()
    {
        // Act
        var engines = _factory.GetAllEngines();

        // Assert
        Assert.IsAssignableFrom<IReadOnlyList<IGradingRuleEngine>>(engines);
    }

    [Fact]
    public void GetEngine_ReturnsSameInstanceOnMultipleCalls()
    {
        // Act
        var engine1 = _factory.GetEngine(GradingCompany.PSA);
        var engine2 = _factory.GetEngine(GradingCompany.PSA);

        // Assert - factory should return the same instance
        Assert.Same(engine1, engine2);
    }

    [Fact]
    public void GetAllEngines_ContainsCorrectEngineTypes()
    {
        // Act
        var engines = _factory.GetAllEngines();

        // Assert
        Assert.Contains(engines, e => e is PsaGradingEngine);
        Assert.Contains(engines, e => e is BgsGradingEngine);
        Assert.Contains(engines, e => e is CgcGradingEngine);
        Assert.Contains(engines, e => e is AceGradingEngine);
        Assert.Contains(engines, e => e is SgcGradingEngine);
        Assert.Contains(engines, e => e is TagGradingEngine);
    }

    [Fact]
    public void Constructor_InitializesAllEngines()
    {
        // Act - constructor is called when creating factory
        var factory = new GradingRuleEngineFactory();
        var engines = factory.GetAllEngines();

        // Assert
        Assert.NotEmpty(engines);
        Assert.All(engines, engine => Assert.NotNull(engine));
    }

    [Fact]
    public void GetEngine_WorksAfterGetAllEngines()
    {
        // Arrange
        var allEngines = _factory.GetAllEngines();

        // Act
        var psaEngine = _factory.GetEngine(GradingCompany.PSA);

        // Assert
        Assert.NotNull(psaEngine);
        Assert.Equal(GradingCompany.PSA, psaEngine.Company);
    }
}
