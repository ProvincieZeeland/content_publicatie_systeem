using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IPublicationRepository
    {
        Task<List<string>> GetObjectIdsAsync();

        Task SaveObjectIdAsync(string objectId);

        Task DeleteObjectIdAsync(string objectId);
    }

    public class PublicationRepository : IPublicationRepository
    {
        private readonly StorageTableService _storageTableService;

        private readonly GlobalSettings _globalSettings;

        public PublicationRepository(
            StorageTableService storageTableService,
            IOptions<GlobalSettings> settings)
        {
            _storageTableService = storageTableService;
            _globalSettings = settings.Value;
        }

        #region Get

        public async Task<List<string>> GetObjectIdsAsync()
        {
            var table = GetToBePublishedTable();
            return await GetObjectIdsAsync(table);
        }

        public async Task<List<string>> GetObjectIdsAsync(CloudTable toBePublishedTable)
        {
            var entries = await GetToBePublishedEntitiesAsync(toBePublishedTable);
            return entries.Select(entry => entry.ObjectId).ToList();
        }

        public async Task<List<ToBePublishedEntity>> GetToBePublishedEntitiesAsync(CloudTable toBePublishedTable)
        {
            var query = new TableQuery<ToBePublishedEntity>();
            var result = await toBePublishedTable.ExecuteQuerySegmentedAsync(query, null);
            if (result == null)
            {
                throw new CpsException($"Error while getting entities from table \"{_globalSettings.ToBePublishedTableName}\"");
            }
            return result.Results.ToList();
        }

        #endregion

        #region Save and Delete

        public async Task SaveObjectIdAsync(string objectId)
        {
            var table = GetToBePublishedTable();
            var entity = new ToBePublishedEntity(_globalSettings.ToBePublishedPartitionKey, objectId);
            await _storageTableService.SaveAsync(table, entity);
        }

        public async Task DeleteObjectIdAsync(string objectId)
        {
            var table = GetToBePublishedTable();
            var entity = new ToBePublishedEntity(_globalSettings.ToBePublishedPartitionKey, objectId) { ETag = "*" };
            await _storageTableService.DeleteAsync(table, entity);
        }

        #endregion

        #region Helpers

        private CloudTable GetToBePublishedTable()
        {
            var table = _storageTableService.GetTable(_globalSettings.ToBePublishedTableName);
            if (table == null)
            {
                throw new CpsException($"Table \"{_globalSettings.ToBePublishedTableName}\" not found");
            }
            return table;
        }

        #endregion
    }
}
