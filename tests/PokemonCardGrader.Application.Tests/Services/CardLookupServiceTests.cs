using NSubstitute;
using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Application.Services;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.Tests.Services;

public sealed class CardLookupServiceTests
{
    private readonly IPokemonTcgApiClient _apiClient;
    private readonly IPokemonCardRepository _cardRepository;
    private readonly CardLookupService _sut;

    public CardLookupServiceTests()
    {
        _apiClient = Substitute.For<IPokemonTcgApiClient>();
        _cardRepository = Substitute.For<IPokemonCardRepository>();
        _sut = new CardLookupService(_apiClient, _cardRepository);
    }

    [Fact]
    public async Task SearchCardsAsync_CallsApiClient_ReturnsResults()
    {
        // Arrange
        var expectedCards = new List<PokemonCardDto>
        {
            new()
            {
                TcgApiId = "xy1-1",
                Name = "Pikachu",
                SetName = "XY Base",
                SetCode = "xy1",
                Number = "1",
                Rarity = "common"
            }
        };
        _apiClient.SearchCardsAsync("pikachu", 1, 20, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedCards));

        // Act
        var result = await _sut.SearchCardsAsync("pikachu", 1);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Pikachu", result[0].Name);
        await _apiClient.Received(1).SearchCardsAsync("pikachu", 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchCardsAsync_WithEmptyQuery_ReturnsEmptyList()
    {
        // Arrange
        _apiClient.SearchCardsAsync("", 1, 20, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<PokemonCardDto>()));

        // Act
        var result = await _sut.SearchCardsAsync("", 1);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchCardsAsync_PropagatesCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _apiClient.SearchCardsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), cts.Token)
            .Returns(Task.FromResult(new List<PokemonCardDto>()));

        // Act
        await _sut.SearchCardsAsync("test", 1, cts.Token);

        // Assert
        await _apiClient.Received(1).SearchCardsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), cts.Token);
    }

    [Fact]
    public async Task GetSetsAsync_CallsApiClient_ReturnsResults()
    {
        // Arrange
        var expectedSets = new List<CardSetDto>
        {
            new()
            {
                Id = "xy1",
                Name = "XY Base",
                Series = "XY",
                TotalCards = 146
            }
        };
        _apiClient.GetSetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedSets));

        // Act
        var result = await _sut.GetSetsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("XY Base", result[0].Name);
    }

    [Fact]
    public async Task GetCardsBySetAsync_CallsApiClient_ReturnsResults()
    {
        // Arrange
        var expectedCards = new List<PokemonCardDto>
        {
            new()
            {
                TcgApiId = "xy1-1",
                Name = "Pikachu",
                SetName = "XY Base",
                SetCode = "xy1",
                Number = "1",
                Rarity = "common"
            }
        };
        _apiClient.GetCardsBySetAsync("xy1", 1, 20, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedCards));

        // Act
        var result = await _sut.GetCardsBySetAsync("xy1", 1);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("xy1", result[0].SetCode);
    }

    [Fact]
    public async Task GetOrCreateCardAsync_WhenCardExists_ReturnsExistingCard()
    {
        // Arrange
        var existingCard = PokemonCard.CreateFromApi(
            "xy1-1", "Pikachu", "XY Base", "xy1", "1", CardRarity.Common, null);
        _cardRepository.GetByTcgApiIdAsync("xy1-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCard?>(existingCard));

        // Act
        var result = await _sut.GetOrCreateCardAsync("xy1-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Pikachu", result.Name);
        Assert.Equal("xy1-1", result.TcgApiId);
        await _cardRepository.Received(1).GetByTcgApiIdAsync("xy1-1", Arg.Any<CancellationToken>());
        await _apiClient.DidNotReceive().GetCardByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cardRepository.DidNotReceive().AddAsync(Arg.Any<PokemonCard>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateCardAsync_WhenCardNotExists_CreatesNewCard()
    {
        // Arrange
        var dto = new PokemonCardDto
        {
            TcgApiId = "xy1-1",
            Name = "Pikachu",
            SetName = "XY Base",
            SetCode = "xy1",
            Number = "1",
            Rarity = "common",
            ImageUrlSmall = "small.jpg",
            ImageUrlLarge = "large.jpg"
        };

        _cardRepository.GetByTcgApiIdAsync("xy1-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCard?>(null));
        _apiClient.GetCardByIdAsync("xy1-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCardDto?>(dto));

        _cardRepository.AddAsync(Arg.Any<PokemonCard>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<PokemonCard>()));

        // Act
        var result = await _sut.GetOrCreateCardAsync("xy1-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Pikachu", result.Name);
        Assert.Equal("xy1-1", result.TcgApiId);
        Assert.Equal("large.jpg", result.ImageUrl);
        await _cardRepository.Received(1).AddAsync(Arg.Any<PokemonCard>(), Arg.Any<CancellationToken>());
        await _cardRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateCardAsync_WhenApiReturnsNull_ThrowsException()
    {
        // Arrange
        _cardRepository.GetByTcgApiIdAsync("invalid", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCard?>(null));
        _apiClient.GetCardByIdAsync("invalid", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCardDto?>(null));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetOrCreateCardAsync("invalid"));
        Assert.Contains("not found", ex.Message);
    }

    [Theory]
    [InlineData("common", CardRarity.Common)]
    [InlineData("Common", CardRarity.Common)]
    [InlineData("COMMON", CardRarity.Common)]
    [InlineData("uncommon", CardRarity.Uncommon)]
    [InlineData("rare", CardRarity.Rare)]
    [InlineData("rare holo", CardRarity.RareHolo)]
    [InlineData("rare ultra", CardRarity.RareUltra)]
    [InlineData("rare secret", CardRarity.RareSecret)]
    [InlineData("rare rainbow", CardRarity.RareRainbow)]
    [InlineData("promo", CardRarity.Promo)]
    [InlineData("unknown", CardRarity.Other)]
    [InlineData("", CardRarity.Other)]
    public async Task GetOrCreateCardAsync_ParsesRarityCorrectly(string rarityString, CardRarity expectedRarity)
    {
        // Arrange
        var dto = new PokemonCardDto
        {
            TcgApiId = "test-1",
            Name = "Test Card",
            SetName = "Test Set",
            SetCode = "test",
            Number = "1",
            Rarity = rarityString
        };

        _cardRepository.GetByTcgApiIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCard?>(null));
        _apiClient.GetCardByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCardDto?>(dto));

        // Act
        var result = await _sut.GetOrCreateCardAsync("test-1");

        // Assert
        Assert.Equal(expectedRarity, result.Rarity);
    }

    [Fact]
    public async Task GetOrCreateCardAsync_PreferLargeImageUrl()
    {
        // Arrange
        var dto = new PokemonCardDto
        {
            TcgApiId = "test-1",
            Name = "Test",
            SetName = "Test Set",
            SetCode = "test",
            Number = "1",
            Rarity = "common",
            ImageUrlSmall = "small.jpg",
            ImageUrlLarge = "large.jpg"
        };

        _cardRepository.GetByTcgApiIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCard?>(null));
        _apiClient.GetCardByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCardDto?>(dto));

        // Act
        var result = await _sut.GetOrCreateCardAsync("test-1");

        // Assert
        Assert.Equal("large.jpg", result.ImageUrl);
    }

    [Fact]
    public async Task GetOrCreateCardAsync_FallbackToSmallImageUrl_WhenLargeIsNull()
    {
        // Arrange
        var dto = new PokemonCardDto
        {
            TcgApiId = "test-1",
            Name = "Test",
            SetName = "Test Set",
            SetCode = "test",
            Number = "1",
            Rarity = "common",
            ImageUrlSmall = "small.jpg",
            ImageUrlLarge = null
        };

        _cardRepository.GetByTcgApiIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCard?>(null));
        _apiClient.GetCardByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PokemonCardDto?>(dto));

        // Act
        var result = await _sut.GetOrCreateCardAsync("test-1");

        // Assert
        Assert.Equal("small.jpg", result.ImageUrl);
    }

    [Fact]
    public async Task CreateManualCardAsync_CreatesCardWithManualFlag()
    {
        // Arrange
        _cardRepository.AddAsync(Arg.Any<PokemonCard>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<PokemonCard>()));

        // Act
        var result = await _sut.CreateManualCardAsync(
            "Charizard", "Base Set", "base1", "4", CardRarity.RareHolo);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Charizard", result.Name);
        Assert.Equal("Base Set", result.SetName);
        Assert.Equal("base1", result.SetCode);
        Assert.Equal("4", result.Number);
        Assert.Equal(CardRarity.RareHolo, result.Rarity);
        Assert.True(result.IsManualEntry);
        Assert.Null(result.TcgApiId);
        await _cardRepository.Received(1).AddAsync(Arg.Any<PokemonCard>(), Arg.Any<CancellationToken>());
        await _cardRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateManualCardAsync_PropagatesCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act
        await _sut.CreateManualCardAsync("Test", "Set", "code", "1", CardRarity.Common, cts.Token);

        // Assert
        await _cardRepository.Received(1).AddAsync(Arg.Any<PokemonCard>(), cts.Token);
        await _cardRepository.Received(1).SaveChangesAsync(cts.Token);
    }
}
