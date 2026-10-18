using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Api.Documents;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "Documents";

    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";

    /// <summary>Prefix for container names; lets test runs use isolated containers.</summary>
    public string ContainerPrefix { get; set; } = string.Empty;

    public int MaxDocumentBytes { get; set; } = 8 * 1024 * 1024;
}

/// <summary>Identity documents and selfies. Stored per market partition; never in SQL, logs or messages (ADR-0009).</summary>
public interface IDocumentStore
{
    Task SaveAsync(string market, string key, byte[] content, CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(string market, string key, CancellationToken cancellationToken);
}

/// <summary>Blob storage with one container per market; in production one storage account per country.</summary>
public sealed class BlobDocumentStore(BlobServiceClient blobs, IOptions<DocumentStorageOptions> options) : IDocumentStore
{
    private readonly ConcurrentDictionary<string, Lazy<Task<BlobContainerClient>>> _containers = new(StringComparer.Ordinal);

    public async Task SaveAsync(string market, string key, byte[] content, CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(market);
        await container.GetBlobClient(key).UploadAsync(new BinaryData(content), overwrite: true, cancellationToken);
    }

    public async Task<byte[]> ReadAsync(string market, string key, CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(market);
        var download = await container.GetBlobClient(key).DownloadContentAsync(cancellationToken);
        return download.Value.Content.ToArray();
    }

    private Task<BlobContainerClient> ContainerAsync(string market) =>
        _containers.GetOrAdd(market, m => new Lazy<Task<BlobContainerClient>>(async () =>
        {
            var container = blobs.GetBlobContainerClient($"{options.Value.ContainerPrefix}documents-{m}".ToLowerInvariant());
            await container.CreateIfNotExistsAsync();
            return container;
        })).Value;
}
