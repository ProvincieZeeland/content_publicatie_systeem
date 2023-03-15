using System;
using System.Threading.Tasks;
using CPS_Jobs.Helpers;
using CPS_Jobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_Jobs.Repositories
{
    public interface ISettingsRepository
    {
        Task<bool> SaveSettingAsync(SettingsEntity setting);
    }

    public class SettingsRepository : ISettingsRepository
    {
        private readonly StorageTableService _storageTableService;

        private readonly IConfiguration _configuration;

        public SettingsRepository(StorageTableService storageTableService,
                                  IConfiguration configuration)
        {
            _storageTableService = storageTableService;
            _configuration = configuration;
        }

        private CloudTable? GetSettingsTable()
        {
            var settingsTableName = _configuration.GetValue<string>("Settings:SettingsTableName");
            var table = this._storageTableService.GetTable(settingsTableName);
            if (table == null)
            {
                throw new Exception($"Tabel \"{settingsTableName}\" not found");
            }
            return table;
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
