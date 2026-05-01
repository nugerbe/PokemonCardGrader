namespace PokemonCardGrader.Application.Interfaces;

public interface IImageStorageService
{
    Task<string> SaveImageAsync(Stream imageStream, string fileName, CancellationToken ct = default);
    Task<Stream?> GetImageAsync(string storagePath, CancellationToken ct = default);
    Task DeleteImageAsync(string storagePath, CancellationToken ct = default);
    string GetImageUrl(string storagePath);
}
