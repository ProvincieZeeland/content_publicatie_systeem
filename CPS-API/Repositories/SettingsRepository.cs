using System.Diagnostics;
using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface ISettingsRepository
    {
        Task<long?> GetSequenceNumberAsync();

        Task<DateTime?> GetLastSynchronisationNewAsync();

        Task<DateTime?> GetLastSynchronisationChangedAsync();

        Task<Dictionary<string, string>> GetLastTokensForNewAsync();

        Task<Dictionary<string, string>> GetLastTokensForChangedAsync();

        Task<Dictionary<string, string>> GetLastTokensForDeletedAsync();

        Task<bool?> GetIsNewSynchronisationRunningAsync();

        Task<bool?> GetIsChangedSynchronisationRunningAsync();

        Task<bool?> GetIsDeletedSynchronisationRunningAsync();

        Task<bool> SaveSettingAsync(SettingsEntity setting);

        Task<long?> SaveSequenceNumberAsync();
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
            var table = _storageTableService.GetTable(_globalSettings.SettingsTableName);
            if (table == null)
            {
                throw new Exception($"Tabel \"{_globalSettings.SettingsTableName}\" not found");
            }
            return table;
        }

        public async Task<long?> GetSequenceNumberAsync()
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return null;

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsSequenceRowKey, settingsTable);
            if (currentSetting == null) throw new Exception("Error while getting SequenceNumber");
            return currentSetting.SequenceNumber;
        }

        public async Task<DateTime?> GetLastSynchronisationNewAsync()
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return null;

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastSynchronisationNewRowKey, settingsTable);
            if (currentSetting == null) return null;
            return currentSetting.LastSynchronisationNew;
        }

        public async Task<DateTime?> GetLastSynchronisationChangedAsync()
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return null;

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastSynchronisationChangedRowKey, settingsTable);
            if (currentSetting == null) return null;
            return currentSetting.LastSynchronisationChanged;
        }

        public async Task<Dictionary<string, string>> GetLastTokensForNewAsync()
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return new Dictionary<string, string>();

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastTokenForNewRowKey, settingsTable);
            if (currentSetting == null) return new Dictionary<string, string>();
            return currentSetting.LastTokenForNew.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(part => part.Split('='))
               .ToDictionary(split => split[0], split => split[1]);
        }

        public async Task<Dictionary<string, string>> GetLastTokensForChangedAsync()
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return new Dictionary<string, string>();

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastTokenForChangedRowKey, settingsTable);
            if (currentSetting == null) return new Dictionary<string, string>();
            return currentSetting.LastTokenForChanged.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(part => part.Split('='))
               .ToDictionary(split => split[0], split => split[1]);
        }

        public async Task<Dictionary<string, string>> GetLastTokensForDeletedAsync()
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return new Dictionary<string, string>();

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastTokenForDeletedRowKey, settingsTable);
            if (currentSetting == null) return new Dictionary<string, string>();
            return currentSetting.LastTokenForDeleted.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(part => part.Split('='))
               .ToDictionary(split => split[0], split => split[1]);
        }

        public async Task<bool?> GetIsNewSynchronisationRunningAsync()
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return null;

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsIsNewSynchronisationRunningRowKey, settingsTable);
            if (currentSetting == null) return null;
            return currentSetting.IsNewSynchronisationRunning;
        }

        public async Task<bool?> GetIsChangedSynchronisationRunningAsync()
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return null;

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsIsChangedSynchronisationRunningRowKey, settingsTable);
            if (currentSetting == null) return null;
            return currentSetting.IsChangedSynchronisationRunning;
        }

        public async Task<bool?> GetIsDeletedSynchronisationRunningAsync()
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return null;

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsIsDeletedSynchronisationRunningRowKey, settingsTable);
            if (currentSetting == null) return null;
            return currentSetting.IsDeletedSynchronisationRunning;
        }

        public async Task<bool> SaveSettingAsync(SettingsEntity setting)
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null)
            {
                return false;
            }

            await _storageTableService.SaveAsync(settingsTable, setting);
            return true;
        }

        public async Task<long?> SaveSequenceNumberAsync()
        {
            var leaseContainer = await _storageTableService.GetLeaseContainer();
            if (leaseContainer == null)
            {
                throw new Exception("Error while getting leaseContainer");
            }
            var settingsTable = GetSettingsTable();
            if (settingsTable == null)
            {
                throw new Exception("Error while getting settingsTable");
            }

            var setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsSequenceRowKey);

            // Try to get the new sequence number and saving it in the storage table.
            var s = new Stopwatch();
            s.Start();
            while (s.Elapsed < TimeSpan.FromSeconds(30))
            {
                try
                {
                    setting.SequenceNumber = await GetSequenceNumberAsync() + 1;
                    await _storageTableService.SaveAsyncWithLease(leaseContainer, settingsTable, _globalSettings.SettingsPartitionKey, setting);
                    return setting.SequenceNumber;
                }
                catch (AcquiringLeaseException ex)
                {

                }
                catch (StorageException ex)
                {
                    // Lease still active? (status 409 or 412)
                    // Then try again for 30 seconds.
                    if (ex.RequestInformation.HttpStatusCode != 409 && ex.RequestInformation.HttpStatusCode != 412)
                    {
                        throw;
                    }
                }
            }
            s.Stop();
            return null;
        }
    }
}
