using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IDriveRepository
    {
        Task<Site> GetSiteAsync(string siteId, bool getAsUser = false);

        Task<Drive> GetDriveAsync(string driveId, bool getAsUser = false);

        Task<Drive> GetDriveAsync(string siteId, string listId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemAsync(string driveId, string driveItemId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemIdsAsync(string driveId, string driveItemId, bool getAsUser = false);

        Task<DriveItem?> CreateAsync(string driveId, string fileName, Stream fileStream, bool getAsUser = false);

        Task DeleteFileAsync(string driveId, string driveItemId, bool getAsUser = false);

        Task<List<string>> GetKnownDrivesAsync();

        Task<List<DeltaDriveItem>> GetNewItems(DateTime startDate, bool getAsUser = false);

        Task<List<DeltaDriveItem>> GetUpdatedItems(DateTime startDate, bool getAsUser = false);

        Task<List<DeltaDriveItem>> GetDeletedItems(DateTime startDate, bool getAsUser = false);

        Task<Stream> DownloadAsync(string driveId, string driveItemId, bool getAsUser = false);
    }

    public class DriveRepository : IDriveRepository
    {
        private readonly GraphServiceClient _graphClient;

        private readonly StorageTableService _storageTableService;

        public DriveRepository(GraphServiceClient graphClient,
                               StorageTableService storageTableService)
        {
            _graphClient = graphClient;
            _storageTableService = storageTableService;
        }

        public async Task<Site> GetSiteAsync(string siteId, bool getAsUser = false)
        {
            var request = _graphClient.Sites[siteId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<Drive> GetDriveAsync(string siteId, string listId, bool getAsUser = false)
        {
            var request = _graphClient.Sites[siteId].Lists[listId].Drive.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<Drive> GetDriveAsync(string driveId, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false)
        {
            var request = _graphClient.Sites[siteId].Lists[listId].Items[listItemId].DriveItem.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.Select("*").GetAsync();
        }

        public async Task<DriveItem> GetDriveItemAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Items[driveItemId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<DriveItem> GetDriveItemIdsAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Items[driveItemId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.Select("sharepointids").GetAsync();
        }

        public async Task<List<string>> GetKnownDrivesAsync()
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();

            var result = await objectIdentifiersTable.ExecuteQuerySegmentedAsync(new TableQuery<ObjectIdentifiersEntity>(), null);
            var objectIdentifiersEntities = result.Results;
            if (objectIdentifiersEntities == null)
            {
                return null;
            }

            return objectIdentifiersEntities.Where(item => item.DriveId != null).Select(item => item.DriveId).Distinct().ToList();
        }

        public async Task<DriveItem?> CreateAsync(string driveId, string fileName, Stream fileStream, bool getAsUser = false)
        {
            if (fileStream.Length > 0)
            {
                var properties = new DriveItemUploadableProperties() { ODataType = null, AdditionalData = new Dictionary<string, object>() };
                properties.AdditionalData.Add("@microsoft.graph.conflictBehavior", "fail");

                var request = _graphClient.Drives[driveId].Root
                    .ItemWithPath(fileName).CreateUploadSession(properties)
                    .Request();
                if (!getAsUser)
                {
                    request = request.WithAppOnly();
                }
                var uploadSession = await request.PostAsync();

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

        public async Task DeleteFileAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Items[driveItemId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            await request.DeleteAsync();
        }

        public async Task<List<DeltaDriveItem>> GetNewItems(DateTime startDate, bool getAsUser = false)
        {
            var driveItems = await GetDeltaAsync(startDate, getAsUser);
            var newItems = driveItems.Where(item => item.Deleted == null && item.CreatedDateTime >= startDate).ToList();
            newItems = newItems.Where(item => item.Folder == null).ToList();
            newItems = newItems.OrderBy(item => item.CreatedDateTime).ToList();
            return newItems;
        }

        public async Task<List<DeltaDriveItem>> GetUpdatedItems(DateTime startDate, bool getAsUser = false)
        {
            var driveItems = await GetDeltaAsync(startDate, getAsUser);
            var updatedItems = driveItems.Where(item => item.Deleted == null && item.CreatedDateTime < startDate).ToList();
            updatedItems = updatedItems.Where(item => item.Folder == null).ToList();
            updatedItems = updatedItems.OrderBy(item => item.LastModifiedDateTime).ToList();
            return updatedItems;
        }

        public async Task<List<DeltaDriveItem>> GetDeletedItems(DateTime startDate, bool getAsUser = false)
        {
            var driveItems = await GetDeltaAsync(startDate, getAsUser);
            var deletedItems = driveItems.Where(item => item.Deleted != null).ToList();
            deletedItems = deletedItems.Where(item => item.Folder == null).ToList();
            deletedItems = deletedItems.OrderBy(item => item.LastModifiedDateTime).ToList();
            return deletedItems;
        }

        private async Task<List<DeltaDriveItem>> GetDeltaAsync(DateTime startDate, bool getAsUser = false)
        {
            // Get known drives
            var driveIds = await GetKnownDrivesAsync();
            if (driveIds.IsNullOrEmpty()) throw new Exception("Drives not found");

            // For each drive:
            // Call graph delta and get changed items since time
            var driveItems = new List<DeltaDriveItem>();
            foreach (var driveId in driveIds)
            {
                var queryOptions = new List<QueryOption>()
                {
                    new QueryOption("token", startDate.ToString("yyyy-MM-ddTHH:mm:ss.fffffff"))
                };

                IDriveItemDeltaCollectionPage delta;
                try
                {
                    var request = _graphClient.Drives[driveId].Root.Delta().Request(queryOptions);
                    if (!getAsUser)
                    {
                        request = request.WithAppOnly();
                    }
                    delta = await request.GetAsync();
                    driveItems.AddRange(delta.CurrentPage.Select(i => MapDriveItemToDeltaItem(driveId, i)));

                    // Fetch additional pages for delta; we get max of 500 per request by default
                    while (delta.NextPageRequest != null)
                    {
                        var newPageRequest = delta.NextPageRequest;
                        if (!getAsUser)
                        {
                            newPageRequest = delta.NextPageRequest.WithAppOnly();
                        }
                        delta = await newPageRequest.GetAsync();
                        driveItems.AddRange(delta.CurrentPage.Select(i => MapDriveItemToDeltaItem(driveId, i)));
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Error while getting changed driveItems with delta");
                }
            }


            return driveItems;
        }

        private DeltaDriveItem MapDriveItemToDeltaItem(string driveId, DriveItem item)
        {
            return new DeltaDriveItem
            {
                Id = item.Id,
                DriveId = driveId,
                Name = item.Name,
                Folder = item.Folder,
                CreatedDateTime = item.CreatedDateTime,
                LastModifiedDateTime = item.LastModifiedDateTime,
                Deleted = item.Deleted
            };
        }

        private CloudTable? GetObjectIdentifiersTable()
        {
            var table = _storageTableService.GetTable(Helpers.Constants.ObjectIdentifiersTableName);
            if (table == null)
            {
                throw new Exception($"Tabel \"{Helpers.Constants.ObjectIdentifiersTableName}\" not found");
            }
            return table;
        }

        public async Task<Stream> DownloadAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Items[driveItemId].Content.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }
    }
}
