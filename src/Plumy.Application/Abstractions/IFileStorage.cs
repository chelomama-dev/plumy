namespace Plumy.Application.Abstractions;

/// <summary>
/// Where uploaded files live (Azure Blob Storage; Azurite locally). The database only keeps their path.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores a new file. Never overwrites: a path that already exists is an error.</summary>
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>The file's content, or null if there is nothing at that path. The caller disposes the stream.</summary>
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken = default);
}
