﻿using CPS_API.Helpers;
using CPS_API.Models;
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

        public SettingsRepository(StorageTableService storageTableService)
        {
            this._storageTableService = storageTableService;
        }

        private CloudTable? GetSettingsTable()
        {
            var table = this._storageTableService.GetTable(Constants.SettingsTableName);
            if (table == null)
            {
                throw new Exception($"Tabel \"{Helpers.Constants.SettingsTableName}\" not found");
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

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(Constants.SettingsPartitionKey, Constants.SettingsSequenceRowKey, settingsTable);
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

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(Constants.SettingsPartitionKey, Constants.SettingsLastSynchronisationNewRowKey, settingsTable);
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

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(Constants.SettingsPartitionKey, Constants.SettingsLastSynchronisationChangedRowKey, settingsTable);
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

            var currentSetting = await this._storageTableService.GetAsync<SettingsEntity>(Constants.SettingsPartitionKey, Constants.SettingsLastSynchronisationDeletedRowKey, settingsTable);
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
