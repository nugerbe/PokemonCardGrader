using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Domain.Tests.Entities;

public sealed class PokemonCardTests
{
    [Fact]
    public void CreateFromApi_SetsAllProperties()
    {
        var card = PokemonCard.CreateFromApi(
            "xy1-1", "Pikachu", "XY Base", "xy1", "1", CardRarity.Common, "img.jpg");

        Assert.NotEqual(Guid.Empty, card.Id);
        Assert.Equal("xy1-1", card.TcgApiId);
        Assert.Equal("Pikachu", card.Name);
        Assert.Equal("XY Base", card.SetName);
        Assert.Equal("xy1", card.SetCode);
        Assert.Equal("1", card.Number);
        Assert.Equal(CardRarity.Common, card.Rarity);
        Assert.Equal("img.jpg", card.ImageUrl);
        Assert.False(card.IsManualEntry);
        Assert.True(card.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void CreateFromApi_WithNullImageUrl_SetsImageUrlToNull()
    {
        var card = PokemonCard.CreateFromApi(
            "xy1-1", "Pikachu", "XY Base", "xy1", "1", CardRarity.Common, null);

        Assert.Null(card.ImageUrl);
    }

    [Fact]
    public void CreateFromApi_GeneratesUniqueIds()
    {
        var card1 = PokemonCard.CreateFromApi("a", "A", "S", "s", "1", CardRarity.Common, null);
        var card2 = PokemonCard.CreateFromApi("b", "B", "S", "s", "2", CardRarity.Common, null);

        Assert.NotEqual(card1.Id, card2.Id);
    }

    [Fact]
    public void CreateManual_SetsIsManualEntryTrue()
    {
        var card = PokemonCard.CreateManual("Charizard", "Base Set", "base1", "4", CardRarity.RareHolo);

        Assert.True(card.IsManualEntry);
        Assert.Null(card.TcgApiId);
        Assert.Null(card.ImageUrl);
    }

    [Fact]
    public void CreateManual_SetsAllProperties()
    {
        var card = PokemonCard.CreateManual("Charizard", "Base Set", "base1", "4", CardRarity.RareHolo);

        Assert.NotEqual(Guid.Empty, card.Id);
        Assert.Equal("Charizard", card.Name);
        Assert.Equal("Base Set", card.SetName);
        Assert.Equal("base1", card.SetCode);
        Assert.Equal("4", card.Number);
        Assert.Equal(CardRarity.RareHolo, card.Rarity);
        Assert.True(card.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(CardRarity.Common)]
    [InlineData(CardRarity.Uncommon)]
    [InlineData(CardRarity.Rare)]
    [InlineData(CardRarity.RareHolo)]
    [InlineData(CardRarity.RareUltra)]
    [InlineData(CardRarity.RareSecret)]
    [InlineData(CardRarity.RareRainbow)]
    [InlineData(CardRarity.Promo)]
    [InlineData(CardRarity.Other)]
    public void CreateFromApi_AcceptsAllRarities(CardRarity rarity)
    {
        var card = PokemonCard.CreateFromApi("id", "Name", "Set", "code", "1", rarity, null);

        Assert.Equal(rarity, card.Rarity);
    }
}
