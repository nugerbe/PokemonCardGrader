using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Phase 22: Optimizes throughput via batch processing and async pipeline management.
/// Processes multiple card images concurrently with bounded parallelism.
/// </summary>
public sealed class BatchProcessor(
    IImageAnalysisService analysisService,
    IOptions<CardAnalysisOptions> options,
    ILogger<BatchProcessor> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    public sealed record BatchItem(
        Guid Id,
        Stream ImageStream,
        ImageType ImageType);

    public sealed record BatchResult(
        Guid Id,
        ImageAnalysisResult? Result,
        string? Error);

    /// <summary>
    /// Processes a batch of images concurrently with bounded parallelism.
    /// Returns results in completion order.
    /// </summary>
    public async IAsyncEnumerable<BatchResult> ProcessBatchAsync(
        IReadOnlyList<BatchItem> items,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (items.Count == 0) yield break;

        var maxParallelism = Math.Max(1, _opts.MaxParallelism);
        var resultChannel = Channel.CreateBounded<BatchResult>(
            new BoundedChannelOptions(items.Count) { SingleReader = true });

        logger.LogInformation(
            "Batch processing {Count} images with parallelism={Parallelism}",
            items.Count, maxParallelism);

        // Producer: process items with bounded concurrency
        var producer = Task.Run(async () =>
        {
            using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(ct);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var result = await analysisService.AnalyzeImageAsync(
                            item.ImageStream, item.ImageType, ct);
                        await resultChannel.Writer.WriteAsync(
                            new BatchResult(item.Id, result, null), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        await resultChannel.Writer.WriteAsync(
                            new BatchResult(item.Id, null, "Cancelled"), ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Batch item {Id} failed.", item.Id);
                        await resultChannel.Writer.WriteAsync(
                            new BatchResult(item.Id, null, ex.Message), ct);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);
            resultChannel.Writer.Complete();
        }, ct);

        // Consumer: yield results as they complete
        await foreach (var result in resultChannel.Reader.ReadAllAsync(ct))
        {
            yield return result;
        }

        await producer; // Ensure any exceptions propagate
    }

    /// <summary>
    /// Processes a batch of images and returns all results when complete.
    /// Respects BatchSize configuration for chunking.
    /// </summary>
    public async Task<List<BatchResult>> ProcessAllAsync(
        IReadOnlyList<BatchItem> items, CancellationToken ct = default)
    {
        var results = new List<BatchResult>(items.Count);
        var batchSize = Math.Max(1, _opts.BatchSize);

        for (var offset = 0; offset < items.Count; offset += batchSize)
        {
            var chunk = items.Skip(offset).Take(batchSize).ToList();

            logger.LogInformation(
                "Processing batch chunk {Start}-{End}/{Total}",
                offset + 1, Math.Min(offset + batchSize, items.Count), items.Count);

            await foreach (var result in ProcessBatchAsync(chunk, ct))
            {
                results.Add(result);
            }
        }

        logger.LogInformation(
            "Batch complete: {Succeeded}/{Total} succeeded",
            results.Count(r => r.Result is not null), results.Count);

        return results;
    }
}
