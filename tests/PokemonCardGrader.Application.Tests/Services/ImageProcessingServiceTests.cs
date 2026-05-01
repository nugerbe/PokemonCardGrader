using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Application.Services;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.Tests.Services;

public sealed class ImageProcessingServiceTests
{
    private readonly IImageStorageService _storageService;
    private readonly ICardSubmissionRepository _submissionRepository;
    private readonly Channel<ImageProcessingRequest> _processingChannel;
    private readonly ImageProcessingService _sut;

    public ImageProcessingServiceTests()
    {
        _storageService = Substitute.For<IImageStorageService>();
        _submissionRepository = Substitute.For<ICardSubmissionRepository>();
        _processingChannel = Channel.CreateUnbounded<ImageProcessingRequest>();

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ICardSubmissionRepository)).Returns(_submissionRepository);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        _sut = new ImageProcessingService(_storageService, scopeFactory, _processingChannel);
    }

    [Fact]
    public async Task UploadAndEnqueueAsync_SavesImageAndEnqueuesRequest()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var userId = "user-123";
        var imageStream = new MemoryStream([1, 2, 3, 4]);
        var fileName = "pikachu-front.jpg";
        var storagePath = "uploads/pikachu-front.jpg";

        _submissionRepository.ExistsAsync(submissionId, userId, Arg.Any<CancellationToken>())
            .Returns(true);
        _storageService.SaveImageAsync(imageStream, fileName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(storagePath));

        // Act
        var result = await _sut.UploadAndEnqueueAsync(
            submissionId, userId, imageStream, fileName, ImageType.Front, 1024);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(submissionId, result.CardSubmissionId);
        Assert.Equal(storagePath, result.StoragePath);
        Assert.Equal(fileName, result.FileName);
        Assert.Equal(ImageType.Front, result.ImageType);
        Assert.Equal(1024, result.FileSizeBytes);

        await _storageService.Received(1).SaveImageAsync(imageStream, fileName, Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).AddImageAsync(Arg.Any<CardImage>(), Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Verify message was enqueued
        var enqueuedRequest = await _processingChannel.Reader.ReadAsync();
        Assert.NotNull(enqueuedRequest);
        Assert.Equal(result.Id, enqueuedRequest.CardImageId);
        Assert.Equal(submissionId, enqueuedRequest.CardSubmissionId);
        Assert.Equal(storagePath, enqueuedRequest.StoragePath);
    }

    [Fact]
    public async Task UploadAndEnqueueAsync_WhenSubmissionNotFound_ThrowsException()
    {
        // Arrange
        _submissionRepository.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var imageStream = new MemoryStream([1, 2, 3]);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UploadAndEnqueueAsync(
                Guid.NewGuid(), "user", imageStream, "test.jpg", ImageType.Front, 100));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task UploadAndEnqueueAsync_AddsImageViaRepository()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var userId = "user-123";
        var imageStream = new MemoryStream([1, 2, 3]);

        _submissionRepository.ExistsAsync(submissionId, userId, Arg.Any<CancellationToken>())
            .Returns(true);
        _storageService.SaveImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("path/to/image.jpg"));

        // Act
        var result = await _sut.UploadAndEnqueueAsync(
            submissionId, userId, imageStream, "image.jpg", ImageType.Back, 2048);

        // Assert
        Assert.Equal(ImageType.Back, result.ImageType);
        Assert.Equal(2048, result.FileSizeBytes);
        await _submissionRepository.Received(1).AddImageAsync(
            Arg.Is<CardImage>(img => img.ImageType == ImageType.Back && img.FileSizeBytes == 2048),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAndEnqueueAsync_PropagatesCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var imageStream = new MemoryStream([1, 2, 3]);

        _submissionRepository.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), cts.Token)
            .Returns(true);
        _storageService.SaveImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), cts.Token)
            .Returns(Task.FromResult("path"));

        // Act
        await _sut.UploadAndEnqueueAsync(
            Guid.NewGuid(), "user", imageStream, "test.jpg", ImageType.Front, 100, cts.Token);

        // Assert
        await _submissionRepository.Received(1).ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), cts.Token);
        await _storageService.Received(1).SaveImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), cts.Token);
        await _submissionRepository.Received(1).SaveChangesAsync(cts.Token);
    }

    [Theory]
    [InlineData(ImageType.Front)]
    [InlineData(ImageType.Back)]
    public async Task UploadAndEnqueueAsync_HandlesAllImageTypes(ImageType imageType)
    {
        // Arrange
        var imageStream = new MemoryStream([1, 2, 3]);

        _submissionRepository.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _storageService.SaveImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("path"));

        // Act
        var result = await _sut.UploadAndEnqueueAsync(
            Guid.NewGuid(), "user", imageStream, "test.jpg", imageType, 100);

        // Assert
        Assert.Equal(imageType, result.ImageType);
    }

    [Fact]
    public async Task UploadAndEnqueueAsync_WithLargeFile_HandlesCorrectly()
    {
        // Arrange
        var largeSize = 10 * 1024 * 1024L; // 10 MB
        var imageStream = new MemoryStream(new byte[largeSize]);

        _submissionRepository.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _storageService.SaveImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("path"));

        // Act
        var result = await _sut.UploadAndEnqueueAsync(
            Guid.NewGuid(), "user", imageStream, "large.jpg", ImageType.Front, largeSize);

        // Assert
        Assert.Equal(largeSize, result.FileSizeBytes);
    }

    [Fact]
    public async Task UploadAndEnqueueAsync_WithEmptyStream_HandlesCorrectly()
    {
        // Arrange
        var imageStream = new MemoryStream();

        _submissionRepository.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _storageService.SaveImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("path"));

        // Act
        var result = await _sut.UploadAndEnqueueAsync(
            Guid.NewGuid(), "user", imageStream, "empty.jpg", ImageType.Front, 0);

        // Assert
        Assert.Equal(0, result.FileSizeBytes);
    }

    [Fact]
    public async Task UploadAndEnqueueAsync_WhenStorageFails_ThrowsException()
    {
        // Arrange
        var imageStream = new MemoryStream([1, 2, 3]);

        _submissionRepository.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _storageService.SaveImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new IOException("Storage failed")));

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(
            () => _sut.UploadAndEnqueueAsync(
                Guid.NewGuid(), "user", imageStream, "test.jpg", ImageType.Front, 100));
    }

    [Fact]
    public async Task UploadAndEnqueueAsync_MultipleImages_EnqueuesInOrder()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var userId = "user-123";

        _submissionRepository.ExistsAsync(submissionId, userId, Arg.Any<CancellationToken>())
            .Returns(true);
        _storageService.SaveImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult($"path/{callInfo.Arg<string>()}"));

        // Act
        var image1 = await _sut.UploadAndEnqueueAsync(
            submissionId, userId, new MemoryStream([1]), "front.jpg", ImageType.Front, 100);
        var image2 = await _sut.UploadAndEnqueueAsync(
            submissionId, userId, new MemoryStream([2]), "back.jpg", ImageType.Back, 200);

        // Assert
        var request1 = await _processingChannel.Reader.ReadAsync();
        var request2 = await _processingChannel.Reader.ReadAsync();

        Assert.Equal(image1.Id, request1.CardImageId);
        Assert.Equal(image2.Id, request2.CardImageId);
        Assert.Equal("path/front.jpg", request1.StoragePath);
        Assert.Equal("path/back.jpg", request2.StoragePath);
    }

    [Fact]
    public async Task UploadAndEnqueueAsync_SpecialCharactersInFileName_HandlesCorrectly()
    {
        // Arrange
        var fileName = "card-#123-pikachü.jpg";
        var imageStream = new MemoryStream([1, 2, 3]);

        _submissionRepository.ExistsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _storageService.SaveImageAsync(imageStream, fileName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("path/to/file"));

        // Act
        var result = await _sut.UploadAndEnqueueAsync(
            Guid.NewGuid(), "user", imageStream, fileName, ImageType.Front, 100);

        // Assert
        Assert.Equal(fileName, result.FileName);
        await _storageService.Received(1).SaveImageAsync(imageStream, fileName, Arg.Any<CancellationToken>());
    }
}
