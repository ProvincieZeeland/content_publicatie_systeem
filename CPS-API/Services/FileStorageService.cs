using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CPS_API.Models;

namespace CPS_API.Helpers
{
    public interface IFileStorageService
    {
        Task CreateAsync(string containerName, string filename, string content, string contenttype, string objectId);

        Task CreateAsync(string containerName, string filename, Stream content, string contenttype, string objectId);

        Task DeleteAsync(string containerName, string objectId, bool deleteIfExists = false);
    }

    public class FileStorageService : IFileStorageService
    {
        private readonly GlobalSettings _globalSettings;

        public FileStorageService(Microsoft.Extensions.Options.IOptions<GlobalSettings> settings)
        {
            _globalSettings = settings.Value;
        }

        public async Task CreateAsync(string containerName, string filename, string content, string contenttype, string objectId)
        {
            await CreateAsync(containerName, filename, contenttype, objectId, contentStr: content);
        }

        public async Task CreateAsync(string containerName, string filename, Stream content, string contenttype, string objectId)
        {
            try
            {
                await CreateAsync(containerName, filename, contenttype, objectId, contentStream: content);
            }
            finally
            {
                content.Close();
                content.Dispose();
            }
        }

        private async Task CreateAsync(string containerName, string filename, string contenttype, string objectId, string? contentStr = null, Stream? contentStream = null)
        {
            var containerClient = await GetBlobContainerClient(containerName);

            var initialMetadata = new Dictionary<string, string> { { "contenttype", contenttype } };
            var tags = new Dictionary<string, string> { { "objectid", objectId } };
            var options = new BlobUploadOptions { Metadata = initialMetadata, Tags = tags };

            BlobClient blobClient = containerClient.GetBlobClient(filename);
            if (contentStr != null)
            {
                await blobClient.UploadAsync(BinaryData.FromString(contentStr), options);
            }
            else if (contentStream != null)
            {
                if (contentStream.Length > 0)
                {
                    contentStream.Position = 0;
                }
                await blobClient.UploadAsync(contentStream, options);
            }
        }

        public async Task DeleteAsync(string containerName, string objectId, bool deleteIfExists = false)
        {
            var containerClient = await GetBlobContainerClient(containerName);
            var taggedBlobItems = FindBlobsByTags(containerClient, objectId);
            if (taggedBlobItems == null || !taggedBlobItems.Any())
            {
                if (deleteIfExists) return;
                throw new FileNotFoundException($"Blob (objectid = {objectId}) does not exist!");
            }

            var blobNames = taggedBlobItems!.Select(blobItem => blobItem.BlobName);
            foreach (var blobName in blobNames)
            {
                var blobClient = containerClient.GetBlobClient(blobName);
                await blobClient.DeleteAsync();
            }
        }

        private static Pageable<TaggedBlobItem>? FindBlobsByTags(BlobContainerClient containerClient, string objectId)
        {
            var query = $"objectid = '{objectId}'";
            return containerClient.FindBlobsByTags(query);
        }

        private string GetConnectionstring()
        {
            return _globalSettings.FileStorageConnectionstring;
        }

        private async Task<BlobContainerClient> GetBlobContainerClient(string containerName)
        {
            var connectionString = GetConnectionstring();
            var containerClient = new BlobContainerClient(connectionString, containerName);
            if (!await containerClient.ExistsAsync())
                throw new FileNotFoundException("Container " + containerName + " does not exist!");
            return containerClient;
        }
    }
}