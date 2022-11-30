using CPS_API.Models;
using Microsoft.Graph;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Helpers
{
    public interface IStorageTableService
    {
        Task<SettingsEntity?> GetCurrentSettingAsync();

        Task<bool> SaveSettingAsync(SettingsEntity setting);

        Task<DocumentsEntity?> GetContentIdsAsync(string contentId);

        Task<bool> SaveContentIdsAsync(string contentId, Drive? drive, DriveItem? driveItem, ContentIds contentIds);
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

        private CloudTable? GetTable(string tableName)
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

        private async Task SaveAsync(CloudTable table, ITableEntity entity)
        {
            var insertop = TableOperation.InsertOrReplace(entity);
            await table.ExecuteAsync(insertop);
        }

        #region Settings

        private CloudTable? GetSettingsTable()
        {
            var table = this.GetTable("settings");
            return table;
        }

        public async Task<SettingsEntity?> GetCurrentSettingAsync()
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return null;
            }

            var currentSetting = await this.GetAsync<SettingsEntity>("0", "0", settingsTable);
            return currentSetting;
        }

        public async Task<bool> SaveSettingAsync(SettingsEntity setting)
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return false;
            }

            await this.SaveAsync(settingsTable, setting);
            return true;
        }

        #endregion

        #region Documents

        private CloudTable? GetDocumentsTable()
        {
            var table = this.GetTable("documents");
            return table;
        }

        public async Task<DocumentsEntity?> GetContentIdsAsync(string contentId)
        {
            var documentsTable = this.GetDocumentsTable();
            if (documentsTable == null)
            {
                return null;
            }

            var documentsEntity = await this.GetAsync<DocumentsEntity>(contentId, contentId, documentsTable);
            return documentsEntity;
        }

        public async Task<bool> SaveContentIdsAsync(string contentId, Drive? drive, DriveItem? driveItem, ContentIds contentIds)
        {
            var documentsTable = this.GetDocumentsTable();
            if (documentsTable == null)
            {
                return false;
            }

            var document = new DocumentsEntity(contentId, drive, driveItem, contentIds);
            await this.SaveAsync(documentsTable, document);
            return true;
        }

        #endregion
    }
}
