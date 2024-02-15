using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IPublicationRepository
    {
        Task<List<ToBePublishedEntity>> GetEntitiesFromQueueAsync();

        Task AddToQueueAsync(string objectId, DateTimeOffset publicationDate);

        Task RemoveFromQueueAsync(ToBePublishedEntity entity);

        Task RemoveFromQueueIfExistsAsync(string objectId);
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

        public async Task<List<ToBePublishedEntity>> GetEntitiesFromQueueAsync()
        {
            var table = GetTable();
            var query = new TableQuery<ToBePublishedEntity>();
            var result = await table.ExecuteQuerySegmentedAsync(query, null);
            if (result == null)
            {
                throw new CpsException($"Error while getting entities from table \"{_globalSettings.ToBePublishedTableName}\"");
            }
            return result.Results?.ToList();
        }

        private async Task<ToBePublishedEntity> GetToBePublishedEntityAsync(CloudTable table, string objectId)
        {
            var filter = TableQuery.GenerateFilterCondition(nameof(TableEntity.RowKey), QueryComparisons.Equal, objectId);
            var query = new TableQuery<ToBePublishedEntity>().Where(filter);
            var result = await table.ExecuteQuerySegmentedAsync(query, null);
            if (result == null)
            {
                throw new CpsException($"Error while getting entities from table \"{_globalSettings.ToBePublishedTableName}\" by \"{objectId}\"");
            }
            return result.Results?.FirstOrDefault();
        }

        #endregion

        #region Save and Delete

        public async Task AddToQueueAsync(string objectId, DateTimeOffset publicationDate)
        {
            var table = GetTable();
            var entity = new ToBePublishedEntity(_globalSettings.ToBePublishedPartitionKey, objectId, publicationDate);
            await _storageTableService.SaveAsync(table, entity);
        }

        public async Task RemoveFromQueueAsync(ToBePublishedEntity entity)
        {
            var table = GetTable();
            await DeleteEntityAsync(table, entity);
        }

        public async Task RemoveFromQueueIfExistsAsync(string objectId)
        {
            var table = GetTable();
            var entity = await GetToBePublishedEntityAsync(table, objectId);
            if (entity == null)
            {
                return;
            }
            await DeleteEntityAsync(table, entity);
        }

        private async Task DeleteEntityAsync(CloudTable table, ToBePublishedEntity entity)
        {
            // Etag * is required for deleting.
            entity.ETag = "*";
            await _storageTableService.DeleteAsync(table, entity);
        }

        #endregion

        #region Helpers

        private CloudTable GetTable()
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
