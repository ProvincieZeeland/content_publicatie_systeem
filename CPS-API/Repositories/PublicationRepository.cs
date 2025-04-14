using System.Net;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IPublicationRepository
    {
        Task<List<ToBePublishedEntity>> GetEntitiesFromQueueAsync();

        Task AddToQueueAsync(string objectId, DateTime publicationDate);

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
            var entities = result.Results?.ToList();
            if (entities == null) throw new CpsException($"Error while getting entities from table \"{_globalSettings.ToBePublishedTableName}\"");
            return entities;
        }

        private async Task<ToBePublishedEntity?> GetToBePublishedEntityAsync(CloudTable table, string objectId)
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

        public async Task AddToQueueAsync(string objectId, DateTime publicationDate)
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
            ToBePublishedEntity? entity;
            try
            {
                entity = await GetToBePublishedEntityAsync(table, objectId);
            }
            catch (StorageException ex) when (ex.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound)
            {
                return;
            }
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