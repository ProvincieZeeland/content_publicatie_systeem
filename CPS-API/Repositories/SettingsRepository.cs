using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface ISettingsRepository
    {
        Task<long?> GetSequenceNumberAsync();

        Task<DateTime?> GetLastSynchronisationAsync();

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

        public async Task<long?> GetSequenceNumberAsync()
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return null;
            }

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(Constants.SettingsPartitionKey, Constants.SettingsSequenceRowKey, settingsTable);
            if (currentSetting == null) throw new Exception("Error while getting SequenceNumber");
            return currentSetting.SequenceNumber;
        }

        public async Task<DateTime?> GetLastSynchronisationAsync()
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return null;
            }

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(Constants.SettingsPartitionKey, Constants.SettingsLastSynchronisationRowKey, settingsTable);
            if (currentSetting == null) throw new Exception("Error while getting LastSynchronisation");
            return currentSetting.LastSynchronisation;
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
