using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface ISettingsRepository
    {
        Task<long?> GetSequenceNumberAsync();

        Task<DateTime?> GetLastSynchronisationNewAsync();

        Task<DateTime?> GetLastSynchronisationChangedAsync();

        Task<DateTime?> GetLastSynchronisationDeletedAsync();

        Task<bool> SaveSettingAsync(SettingsEntity setting);
    }

    public class SettingsRepository : ISettingsRepository
    {
        private readonly StorageTableService _storageTableService;

        private readonly GlobalSettings _globalSettings;

        public SettingsRepository(StorageTableService storageTableService,
                                  IOptions<GlobalSettings> settings)
        {
            _storageTableService = storageTableService;
            _globalSettings = settings.Value;
        }

        private CloudTable? GetSettingsTable()
        {
            var table = this._storageTableService.GetTable(_globalSettings.SettingsTableName);
            if (table == null)
            {
                throw new Exception($"Tabel \"{_globalSettings.SettingsTableName}\" not found");
            }
            return table;
        }

        public async Task<long?> GetSequenceNumberAsync()
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return null;
            }

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsSequenceRowKey, settingsTable);
            if (currentSetting == null) throw new Exception("Error while getting SequenceNumber");
            return currentSetting.SequenceNumber;
        }

        public async Task<DateTime?> GetLastSynchronisationNewAsync()
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return null;
            }

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastSynchronisationNewRowKey, settingsTable);
            if (currentSetting == null) return null;
            return currentSetting.LastSynchronisationNew;
        }

        public async Task<DateTime?> GetLastSynchronisationChangedAsync()
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return null;
            }

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastSynchronisationChangedRowKey, settingsTable);
            if (currentSetting == null) return null;
            return currentSetting.LastSynchronisationChanged;
        }

        public async Task<DateTime?> GetLastSynchronisationDeletedAsync()
        {
            var settingsTable = this.GetSettingsTable();
            if (settingsTable == null)
            {
                return null;
            }

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastSynchronisationDeletedRowKey, settingsTable);
            if (currentSetting == null) return null;
            return currentSetting.LastSynchronisationDeleted;
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
