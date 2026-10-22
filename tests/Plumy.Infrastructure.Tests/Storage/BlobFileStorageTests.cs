using Azure;
using Azure.Storage.Blobs;
using Plumy.Infrastructure.Storage;
using Testcontainers.Azurite;

namespace Plumy.Infrastructure.Tests.Storage;

/// <summary>Starts Azurite (the Blob Storage emulator) once for the class.</summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    // Same version Aspire runs locally. The SDK may speak a newer REST API version than this Azurite knows
    private readonly AzuriteContainer _container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public BlobServiceClient Service { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Service = new BlobServiceClient(_container.GetConnectionString());
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public class BlobFileStorageTests(AzuriteFixture fixture) : IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    private BlobFileStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _storage = new BlobFileStorage(fixture.Service);
        await _storage.CreateContainerIfNotExistsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string NewPath() => $"{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid()}";

    [Fact]
    public async Task Upload_ThenOpenRead_ReturnsSameContentAndType()
    {
        var path = NewPath();
        byte[] content = [.. "%PDF-1.7 hola"u8];

        await _storage.UploadAsync(path, new MemoryStream(content), "application/pdf");

        await using var stream = await _storage.OpenReadAsync(path);
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(content, copy.ToArray());

        var properties = await fixture.Service.GetBlobContainerClient(BlobFileStorage.ContainerName)
            .GetBlobClient(path).GetPropertiesAsync();
        Assert.Equal("application/pdf", properties.Value.ContentType);
    }

    [Fact]
    public async Task OpenRead_MissingFile_ReturnsNull()
    {
        Assert.Null(await _storage.OpenReadAsync(NewPath()));
    }

    [Fact]
    public async Task Upload_ExistingPath_FailsAndKeepsTheOriginal()
    {
        var path = NewPath();
        await _storage.UploadAsync(path, new MemoryStream([1, 2, 3]), "image/png");

        await Assert.ThrowsAsync<RequestFailedException>(() =>
            _storage.UploadAsync(path, new MemoryStream([4, 5, 6]), "image/png"));

        await using var stream = await _storage.OpenReadAsync(path);
        using var copy = new MemoryStream();
        await stream!.CopyToAsync(copy);
        Assert.Equal([1, 2, 3], copy.ToArray());
    }

    [Fact]
    public async Task DeleteIfExists_RemovesTheFile_AndIgnoresMissingOnes()
    {
        var path = NewPath();
        await _storage.UploadAsync(path, new MemoryStream([1]), "image/png");

        await _storage.DeleteIfExistsAsync(path);
        await _storage.DeleteIfExistsAsync(path);

        Assert.Null(await _storage.OpenReadAsync(path));
    }
}
