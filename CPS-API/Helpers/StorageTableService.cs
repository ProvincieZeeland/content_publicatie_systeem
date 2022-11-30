﻿using CPS_API.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Helpers
{
    public interface IStorageTableService
    {
        CloudTable? GetTable(string tableName);

        Task<T?> GetAsync<T>(string partitionKey, string rowKey, CloudTable table) where T : ITableEntity;

        Task SaveAsync(CloudTable table, ITableEntity entity);
    }

    public class StorageTableService : IStorageTableService
    {
        private readonly GlobalSettings _globalSettings;

        public StorageTableService(Microsoft.Extensions.Options.IOptions<GlobalSettings> settings)
        {
            this._globalSettings = settings.Value;
        }

        private string GetConnectionstring()
        {
            return this._globalSettings.StorageTableConnectionstring;
        }

        private CloudTableClient? GetCloudTableClient()
        {
            var connectionString = this.GetConnectionstring();
            var storageAccount = CloudStorageAccount.Parse(connectionString);

            var tableClient = storageAccount.CreateCloudTableClient();
            return tableClient;
        }

        public CloudTable? GetTable(string tableName)
        {
            var tableClient = this.GetCloudTableClient();
            if (tableClient == null)
            {
                return null;
            }
            var table = tableClient.GetTableReference(tableName);
            return table;
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
    }
}
