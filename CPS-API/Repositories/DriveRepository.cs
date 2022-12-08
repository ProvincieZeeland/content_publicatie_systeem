using CPS_API.Models;
using Microsoft.Graph;
using Newtonsoft.Json;

namespace CPS_API.Repositories
{
    public interface IDriveRepository
    {
        Task<Drive> GetDriveAsync(string driveId);

        Task<Drive> GetDriveAsync(string siteId, string listId);

        Task<DriveItem> GetDriveItemAsync(string driveId, string driveItemId);

        Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId);

        Task<DriveItem?> CreateAsync(string driveId, string fileName, Stream fileStream);

        Task DeleteFileAsync(string driveId, string driveItemId);

        Task<IEnumerable<string>> GetKnownDrivesAsync();

        Task<IEnumerable<ContentIds>> GetNewItems(DateTime startDate);

        Task<IEnumerable<ContentIds>> GetUpdatedItems(DateTime startDate);

        Task<IEnumerable<ContentIds>> GetDeletedItems(DateTime startDate);
    }

    public class DriveRepository : IDriveRepository
    {
        private readonly GraphServiceClient _graphClient;

        public DriveRepository(GraphServiceClient graphClient)
        {
            _graphClient = graphClient;
        }

        public async Task<Drive> GetDriveAsync(string siteId, string listId)
        {
            return await _graphClient.Sites[siteId].Lists[listId].Drive.Request().GetAsync();
        }

        public async Task<Drive> GetDriveAsync(string driveId)
        {
            return await _graphClient.Drives[driveId].Request().GetAsync();
        }

        public async Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId)
        {
            return await _graphClient.Sites[siteId].Lists[listId].Items[listItemId].DriveItem.Request().GetAsync();
        }

        public async Task<DriveItem> GetDriveItemAsync(string driveId, string driveItemId)
        {
            return await _graphClient.Drives[driveId].Items[driveItemId].Request().GetAsync();
        }


        public Task<IEnumerable<string>> GetKnownDrivesAsync()
        {
            // Get all unique driveIds from documents table

            throw new NotImplementedException();
        }

        public async Task<DriveItem?> CreateAsync(string driveId, string fileName, Stream fileStream)
        {
            if (fileStream.Length > 0)
            {
                var properties = new DriveItemUploadableProperties() { ODataType = null, AdditionalData = new Dictionary<string, object>() };
                properties.AdditionalData.Add("@microsoft.graph.conflictBehavior", "fail");

                var uploadSession = await _graphClient.Drives[driveId].Root
                    .ItemWithPath(fileName).CreateUploadSession(properties)
                    .Request()
                    .PostAsync();

                // 10 MB; recommended fragment size is between 5-10 MiB
                var chunkSize = (320 * 1024) * 32;
                var fileUploadTask = new LargeFileUploadTask<DriveItem>(uploadSession, fileStream, chunkSize);
                var totalLength = fileStream.Length;

                // Create a callback that is invoked after each slice is uploaded
                IProgress<long> progress = new Progress<long>(prog =>
                {
                    Console.WriteLine($"Uploaded {prog} bytes of {totalLength} bytes");
                });

                DriveItem driveItem = null;
                try
                {
                    // Upload the file
                    var uploadResult = await fileUploadTask.UploadAsync(progress);
                    if (uploadResult.UploadSucceeded)
                        driveItem = uploadResult.ItemResponse;
                }
                catch (ServiceException ex)
                {
                    throw new Exception("Failed to upload file.", ex);
                }

                if (driveItem == null)
                {
                    throw new Exception("Failed to upload file.");
                }

                return driveItem;
            }
            else
            {
                throw new Exception("Cannot upload empty file stream.");
            }
        }

        public async Task DeleteFileAsync(string driveId, string driveItemId)
        {
            await _graphClient.Drives[driveId].Items[driveItemId].Request().DeleteAsync();
        }

        public Task<IEnumerable<ContentIds>> GetNewItems(DateTime startDate)
        {
            // Get known drives

            // For each drive:
            // Call graph delta and get changed items since time
            // https://learn.microsoft.com/en-us/graph/api/driveitem-delta?view=graph-rest-1.0&tabs=http#example-4-retrieving-delta-results-using-a-timestamp
            // https://learn.microsoft.com/en-us/onedrive/developer/rest-api/concepts/scan-guidance?view=odsp-graph-online#recommended-calling-pattern

            // Make list of all new items
            // include at least driveitemid + driveid > can find rest in documents table if other data is not available
            throw new NotImplementedException();
        }

        public Task<IEnumerable<ContentIds>> GetUpdatedItems(DateTime startDate)
        {
            // Get known drives

            // For each drive:
            // Call graph delta and get changed items since time

            // Make list of all updated items
            // include at least driveitemid + driveid > can find rest in documents table if other data is not available
            throw new NotImplementedException();
        }

        public Task<IEnumerable<ContentIds>> GetDeletedItems(DateTime startDate)
        {
            // Get known drives

            // For each drive:
            // Call graph delta and get changed items since time

            // Make list of all deleted items
            // include at least driveitemid + driveid > can find rest in documents table if other data is not available

            throw new NotImplementedException();
        }
    }
}
