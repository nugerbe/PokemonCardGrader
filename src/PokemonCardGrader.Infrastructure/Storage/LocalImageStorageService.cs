using PokemonCardGrader.Application.Interfaces;

namespace PokemonCardGrader.Infrastructure.Storage;

public sealed class LocalImageStorageService(string basePath) : IImageStorageService
{
    public async Task<string> SaveImageAsync(Stream imageStream, string fileName, CancellationToken ct = default)
    {
        var datePath = DateTimeOffset.UtcNow.ToString("yyyy/MM/dd");
        var uniqueName = $"{Guid.NewGuid():N}_{fileName}";
        var relativePath = Path.Combine("uploads", datePath, uniqueName);
        var fullPath = Path.Combine(basePath, relativePath);

        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        await using var fileStream = File.Create(fullPath);
        await imageStream.CopyToAsync(fileStream, ct);

        return relativePath;
    }

    public Task<Stream?> GetImageAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(basePath, storagePath);
        if (!File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteImageAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(basePath, storagePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public string GetImageUrl(string storagePath)
    {
        return $"/{storagePath.Replace('\\', '/')}";
    }
}
