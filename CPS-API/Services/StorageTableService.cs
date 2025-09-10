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

        Task SaveBatchAsync<T>(CloudTable table, List<T> entities) where T : ITableEntity;

        Task DeleteAsync(CloudTable table, ITableEntity entity);

        Task DeleteBatchAsync<T>(CloudTable table, List<T> entities) where T : ITableEntity;

        Task<CloudBlobContainer> GetLeaseContainer();

        Task<CloudQueue> GetQueue(string queueName);

        Task<List<T>> ExecuteQuerySegmentedAsync<T>(CloudTable table, TableQuery<T> query) where T : ITableEntity, new();
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
            var storageAccount = GetStorageAccount(connectionString);
            return storageAccount.CreateCloudTableClient();
        }

        private CloudBlobClient? GetCloudBlobClient()
        {
            var connectionString = GetConnectionstring();
            var storageAccount = GetStorageAccount(connectionString);
            return storageAccount.CreateCloudBlobClient();
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

        public async Task SaveBatchAsync<T>(CloudTable table, List<T> entities) where T : ITableEntity
        {
            var tableBatchOperation = new TableBatchOperation();
            entities.ForEach(entity => tableBatchOperation.Add(TableOperation.InsertOrReplace(entity)));
            await table.ExecuteBatchAsync(tableBatchOperation);
        }

        public async Task DeleteAsync(CloudTable table, ITableEntity entity)
        {
            var delete = TableOperation.Delete(entity);
            await table.ExecuteAsync(delete);
        }

        public async Task DeleteBatchAsync<T>(CloudTable table, List<T> entities) where T : ITableEntity
        {
            var tableBatchOperation = new TableBatchOperation();
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

        private string GetJobsConnectionstring()
        {
            return _globalSettings.JobsStorageTableConnectionstring;
        }

        private CloudQueueClient? GetCloudQueueClient()
        {
            var connectionString = GetJobsConnectionstring();
            var storageAccount = GetStorageAccount(connectionString);
            return storageAccount.CreateCloudQueueClient();
        }

        public async Task<CloudQueue> GetQueue(string queueName)
        {
            var queueClient = GetCloudQueueClient();
            var queue = queueClient.GetQueueReference(queueName);
            await queue.CreateIfNotExistsAsync();
            return queue;
        }

        private static CloudStorageAccount GetStorageAccount(string connectionString)
        {
            return CloudStorageAccount.Parse(connectionString);
        }

        public async Task<List<T>> ExecuteQuerySegmentedAsync<T>(CloudTable table, TableQuery<T> query) where T : ITableEntity, new()
        {
            // Querying the table not based on partitionKey or rowKey sometimes gives other behaviour when accessing large tables.
            // Results are empty in this situation and the item must be found by using the continuationtoken.
            // https://stackoverflow.com/questions/36454984/querying-azure-table-without-partitionkey-and-rowkey
            List<T> results = new List<T>();
            TableContinuationToken? token = null;
            do
            {
                var seg = await table.ExecuteQuerySegmentedAsync(query, token);
                token = seg.ContinuationToken;
                results.AddRange(seg.Results);
            }
            while (token != null && results.Count < 1);
            return results;
        }
    }
}
