using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CPS_API.Models;
using Microsoft.IdentityModel.Tokens;

namespace CPS_API.Helpers
{
    public interface IFileStorageService
    {
        Task<IEnumerable<string>> GetAllAsync(string containerName, string folder);

        Task<string> GetContentAsStringAsync(string containerName, string filename);

        Task<byte[]> GetContentAsync(string containerName, string filename);

        Task CreateAsync(string containerName, string filename, string content, string contenttype, string objectId);

        Task CreateAsync(string containerName, string filename, Stream content, string contenttype, string objectId);

        Task DeleteAsync(string containerName, string objectId);
    }

    public class FileStorageService : IFileStorageService
    {
        private readonly GlobalSettings _globalSettings;

        public FileStorageService(Microsoft.Extensions.Options.IOptions<GlobalSettings> settings)
        {
            _globalSettings = settings.Value;
        }

        private string GetConnectionstring()
        {
            return _globalSettings.FileStorageConnectionstring;
        }

        public async Task<IEnumerable<string>> GetAllAsync(string containerName, string folder)
        {
            string connectionString = GetConnectionstring();
            BlobContainerClient containerClient = new BlobContainerClient(connectionString, containerName);
            if (!await containerClient.ExistsAsync()) throw new FileNotFoundException("Container " + containerName + " does not exist!");

            List<string> files = new List<string>();

            // Call the listing operation and return pages of the specified size.
            var resultSegment = containerClient.GetBlobsByHierarchyAsync(prefix: folder, delimiter: "/").AsPages(default, 500);

            // Enumerate the blobs returned for each page.
            await foreach (Azure.Page<BlobHierarchyItem> blobPage in resultSegment)
            {
                // A hierarchical listing may return both virtual directories and blobs.
                foreach (BlobHierarchyItem blobhierarchyItem in blobPage.Values)
                {
                    if (blobhierarchyItem.IsPrefix)
                    {
                        // Call recursively with the prefix to traverse the virtual directory.
                        IEnumerable<string> subfiles = await GetAllAsync(containerName, blobhierarchyItem.Prefix);
                        files.AddRange(subfiles);
                    }
                    else
                    {
                        files.Add(blobhierarchyItem.Blob.Name);
                    }
                }
            }

            return files;
        }

        public async Task<string> GetContentAsStringAsync(string containerName, string filename)
        {
            string connectionString = GetConnectionstring();
            BlobContainerClient containerClient = new BlobContainerClient(connectionString, containerName);

            if (!await containerClient.ExistsAsync())
                throw new FileNotFoundException("Container " + containerName + " does not exist!");

            BlobClient blobClient = containerClient.GetBlobClient(filename);
            if (!await blobClient.ExistsAsync())
                throw new FileNotFoundException("File " + filename + " does not exist!");

            var response = await blobClient.DownloadAsync();
            using (var streamReader = new StreamReader(response.Value.Content))
            {
                string content = await streamReader.ReadToEndAsync();
                return content;
            }
        }

        public async Task<byte[]> GetContentAsync(string containerName, string filename)
        {
            string connectionString = GetConnectionstring();
            BlobContainerClient containerClient = new BlobContainerClient(connectionString, containerName);

            if (!await containerClient.ExistsAsync())
                throw new FileNotFoundException("Container " + containerName + " does not exist!");

            BlobClient blobClient = containerClient.GetBlobClient(filename);
            if (!await blobClient.ExistsAsync())
                throw new FileNotFoundException("File " + filename + " does not exist!");

            using (var streamReader = new MemoryStream())
            {
                await blobClient.DownloadToAsync(streamReader);
                byte[] content = streamReader.ToArray();
                return content;
            }
        }

        public async Task CreateAsync(string containerName, string filename, string content, string contenttype, string objectId)
        {
            string connectionString = GetConnectionstring();

            BlobContainerClient containerClient = new BlobContainerClient(connectionString, containerName);
            if (!await containerClient.ExistsAsync())
                throw new FileNotFoundException("Container " + containerName + " does not exist!");

            var initialMetadata = new Dictionary<string, string> { { "contenttype", contenttype } };
            var tags = new Dictionary<string, string> { { "objectid", objectId } };
            BlobClient blobClient = containerClient.GetBlobClient(filename);
            await blobClient.UploadAsync(BinaryData.FromString(content), new BlobUploadOptions { Metadata = initialMetadata, Tags = tags });
        }

        public async Task CreateAsync(string containerName, string filename, Stream content, string contenttype, string objectId)
        {
            try
            {
                string connectionString = GetConnectionstring();

                BlobContainerClient containerClient = new BlobContainerClient(connectionString, containerName);
                if (!await containerClient.ExistsAsync())
                    throw new FileNotFoundException("Container " + containerName + " does not exist!");

                var initialMetadata = new Dictionary<string, string> { { "contenttype", contenttype } };
                var tags = new Dictionary<string, string> { { "objectid", objectId } };
                BlobClient blobClient = containerClient.GetBlobClient(filename);
                content.Position = 0;
                await blobClient.UploadAsync(content, new BlobUploadOptions { Metadata = initialMetadata, Tags = tags });
            }
            finally
            {
                content.Close();
                content.Dispose();
            }
        }

        public async Task DeleteAsync(string containerName, string objectId)
        {
            string connectionString = GetConnectionstring();

            var containerClient = new BlobContainerClient(connectionString, containerName);
            if (!await containerClient.ExistsAsync())
                throw new FileNotFoundException("Container " + containerName + " does not exist!");

            var query = $"objectid = '{objectId}'";
            var taggedBlobItems = containerClient.FindBlobsByTags(query);
            if (taggedBlobItems.IsNullOrEmpty()) throw new FileNotFoundException($"Blob (objectid = {objectId}) does not exist!");

            foreach (var blobItem in taggedBlobItems)
            {
                var blobName = blobItem.BlobName;
                var blobClient = containerClient.GetBlobClient(blobName);
                await blobClient.DeleteAsync();
            }
        }
    }
}
