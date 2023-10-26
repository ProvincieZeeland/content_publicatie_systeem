using CPS_API.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Helpers
{
    public interface IStorageTableService
    {
        CloudTable? GetTable(string tableName);

        Task<T?> GetAsync<T>(string partitionKey, string rowKey, CloudTable table) where T : ITableEntity;

        Task SaveAsync(CloudTable table, ITableEntity entity);

        Task DeleteAsync(CloudTable table, List<ITableEntity> entities);

        Task<CloudBlobContainer> GetLeaseContainer();

        Task<CloudQueue> GetQueue(string queueName);
    }

    public class StorageTableService : IStorageTableService
    {
        private readonly GlobalSettings _globalSettings;

        public StorageTableService(Microsoft.Extensions.Options.IOptions<GlobalSettings> settings)
        {
            _globalSettings = settings.Value;
        }

        private string GetConnectionstring()
        {
            return _globalSettings.StorageTableConnectionstring;
        }

        private CloudTableClient? GetCloudTableClient()
        {
            var connectionString = GetConnectionstring();
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            return storageAccount.CreateCloudTableClient();
        }

        private CloudBlobClient? GetCloudBlobClient()
        {
            var connectionString = GetConnectionstring();
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            return storageAccount.CreateCloudBlobClient();
        }

        private CloudQueueClient? GetCloudQueueClient()
        {
            var connectionString = GetConnectionstring();
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            return storageAccount.CreateCloudQueueClient();
        }

        public CloudTable? GetTable(string tableName)
        {
            var tableClient = GetCloudTableClient();
            return tableClient?.GetTableReference(tableName);
        }

        public async Task<T?> GetAsync<T>(string partitionKey, string rowKey, CloudTable table) where T : ITableEntity
        {
            var retrieveOperation = TableOperation.Retrieve<T>(partitionKey, rowKey);
            var result = await table.ExecuteAsync(retrieveOperation);
            if (result == null)
            {
                return default;
            }
            return (T)result.Result;
        }

        public async Task SaveAsync(CloudTable table, ITableEntity entity)
        {
            var insertop = TableOperation.InsertOrReplace(entity);
            await table.ExecuteAsync(insertop);
        }

        public async Task DeleteAsync(CloudTable table, List<ITableEntity> entities)
        {
            TableBatchOperation tableBatchOperation = new TableBatchOperation();
            entities.ForEach(entity => tableBatchOperation.Add(TableOperation.Delete(entity)));
            await table.ExecuteBatchAsync(tableBatchOperation);
        }

        public async Task<CloudBlobContainer> GetLeaseContainer()
        {
            var blobClient = GetCloudBlobClient();
            var leaseContainer = blobClient.GetContainerReference("leaseobjects");
            await leaseContainer.CreateIfNotExistsAsync();
            return leaseContainer;
        }

        public async Task<CloudQueue> GetQueue(string queueName)
        {
            var queueClient = GetCloudQueueClient();
            var queue = queueClient.GetQueueReference(queueName);
            await queue.CreateIfNotExistsAsync();
            return queue;
        }
    }
}
