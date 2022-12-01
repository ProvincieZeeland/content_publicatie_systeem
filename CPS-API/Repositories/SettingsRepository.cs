using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface ISettingsRepository
    {
        Task<SettingsEntity?> GetCurrentSettingAsync();

        Task<bool> SaveSettingAsync(SettingsEntity setting);
    }

    public class SettingsRepository : ISettingsRepository
    {
        private readonly StorageTableService _storageTableService;

        public SettingsRepository(StorageTableService storageTableService)
        {
            this._storageTableService = storageTableService;
        }

        private CloudTable? GetSettingsTable()
        {
            var table = this._storageTableService.GetTable(Constants.SettingsTableName);
            return table;
        }

        public async Task<SettingsEntity?> GetCurrentSettingAsync()
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return null;
            }

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(Constants.SettingsPartitionKey, Constants.SettingsRowKey, settingsTable);
            return currentSetting;
        }

        public async Task<bool> SaveSettingAsync(SettingsEntity setting)
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return false;
            }

            await this._storageTableService.SaveAsync(settingsTable, setting);
            return true;
        }
    }
}
