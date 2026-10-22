using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Plumy.Application.Abstractions;

namespace Plumy.Infrastructure.Storage;

/// <summary>
/// Files in one private Blob Storage container. In Azure the API reaches it with its managed identity (no keys);
/// locally, Azurite.
/// </summary>
internal sealed class BlobFileStorage(BlobServiceClient service) : IFileStorage
{
    public const string ContainerName = "invoices";

    private readonly BlobContainerClient _container = service.GetBlobContainerClient(ContainerName);

    public async Task UploadAsync(
        string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await _container.GetBlobClient(path).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            // If-None-Match: * → the upload fails instead of replacing a file that is already there
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            // Streams from Blob Storage as the client reads, instead of loading the whole file in memory
            var download = await _container.GetBlobClient(path).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return download.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    public Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken = default) =>
        _container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    /// <summary>
    /// Development and tests only, like the database migrations: in Azure the container is declared in Bicep.
    /// </summary>
    public Task CreateContainerIfNotExistsAsync(CancellationToken cancellationToken = default) =>
        _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
}
