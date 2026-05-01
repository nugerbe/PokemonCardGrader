using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;

namespace PokemonCardGrader.Infrastructure.ExternalApis;

public sealed class PokemonTcgApiClientAdapter(
    IHttpClientFactory httpClientFactory,
    ILogger<PokemonTcgApiClientAdapter> logger) : IPokemonTcgApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Characters that must be escaped in Pokemon TCG API Lucene query values.
    // The API returns 404 when these appear unescaped inside a quoted name query.
    private static readonly char[] LuceneSpecialChars = ['\\', '+', '!', '(', ')', '{', '}', '[', ']', '^', '"', '~', '*', '?', ':', '/'];

    private static string EscapeLuceneValue(string value)
    {
        // Apostrophes and hyphens are handled by wrapping in quotes, but
        // other Lucene operators inside the value must be backslash-escaped.
        var sb = new System.Text.StringBuilder(value.Length + 4);
        foreach (var ch in value)
        {
            if (Array.IndexOf(LuceneSpecialChars, ch) >= 0)
                sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }

    public async Task<List<PokemonCardDto>> SearchCardsAsync(string query, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("PokemonTcgApi");
        var luceneQuery = $"name:\"{EscapeLuceneValue(query)}\"";
        var url = $"v2/cards?q={Uri.EscapeDataString(luceneQuery)}&page={page}&pageSize={pageSize}";

        try
        {
            var response = await client.GetFromJsonAsync<TcgApiResponse<TcgCard>>(url, JsonOptions, ct);
            return response?.Data?.Select(MapToDto).ToList() ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching cards with query: {Query}", query);
            return [];
        }
    }

    public async Task<PokemonCardDto?> GetCardByIdAsync(string tcgApiId, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("PokemonTcgApi");
        try
        {
            var response = await client.GetFromJsonAsync<TcgApiSingleResponse<TcgCard>>($"v2/cards/{tcgApiId}", JsonOptions, ct);
            return response?.Data is not null ? MapToDto(response.Data) : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting card by ID: {Id}", tcgApiId);
            return null;
        }
    }

    public async Task<List<CardSetDto>> GetSetsAsync(CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("PokemonTcgApi");
        try
        {
            var response = await client.GetFromJsonAsync<TcgApiResponse<TcgSet>>("v2/sets?orderBy=-releaseDate", JsonOptions, ct);
            return response?.Data?.Select(s => new CardSetDto
            {
                Id = s.Id,
                Name = s.Name,
                Series = s.Series ?? string.Empty,
                TotalCards = s.Total,
                LogoUrl = s.Images?.Logo,
                SymbolUrl = s.Images?.Symbol,
                ReleaseDate = s.ReleaseDate
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting sets.");
            return [];
        }
    }

    public async Task<List<PokemonCardDto>> GetCardsBySetAsync(string setCode, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("PokemonTcgApi");
        var url = $"v2/cards?q=set.id:{Uri.EscapeDataString(setCode)}&page={page}&pageSize={pageSize}";
        try
        {
            var response = await client.GetFromJsonAsync<TcgApiResponse<TcgCard>>(url, JsonOptions, ct);
            return response?.Data?.Select(MapToDto).ToList() ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting cards for set: {SetCode}", setCode);
            return [];
        }
    }

    private static PokemonCardDto MapToDto(TcgCard card) => new()
    {
        TcgApiId = card.Id,
        Name = card.Name,
        SetName = card.Set?.Name ?? string.Empty,
        SetCode = card.Set?.Id ?? string.Empty,
        Number = card.Number ?? string.Empty,
        Rarity = card.Rarity ?? "Unknown",
        ImageUrlSmall = card.Images?.Small,
        ImageUrlLarge = card.Images?.Large,
        Artist = card.Artist
    };

    // Internal DTOs for Pokemon TCG API deserialization
    private sealed record TcgApiResponse<T>(List<T>? Data, int TotalCount);
    private sealed record TcgApiSingleResponse<T>(T? Data);
    private sealed record TcgCard(string Id, string Name, string? Number, string? Rarity, string? Artist, TcgCardSet? Set, TcgCardImages? Images);
    private sealed record TcgCardSet(string Id, string? Name);
    private sealed record TcgCardImages(string? Small, string? Large);
    private sealed record TcgSet(string Id, string Name, string? Series, int Total, string? ReleaseDate, TcgSetImages? Images);
    private sealed record TcgSetImages(string? Logo, string? Symbol);
}
