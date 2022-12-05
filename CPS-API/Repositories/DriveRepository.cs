using CPS_API.Models;
using Microsoft.Graph;

namespace CPS_API.Repositories
{
    public interface IDriveRepository
    {
        Task<Drive> GetDriveAsync(string siteId);

        Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId);

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

        public async Task<Drive> GetDriveAsync(string siteId)
        {
            return await _graphClient.Sites[siteId].Drive.Request().GetAsync();
        }

        public async Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId)
        {
            return await _graphClient.Sites[siteId].Lists[listId].Items[listItemId].DriveItem.Request().GetAsync();
        }

        public Task<IEnumerable<string>> GetKnownDrivesAsync()
        {
            // Get all unique driveIds from documents table

            throw new NotImplementedException();
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
