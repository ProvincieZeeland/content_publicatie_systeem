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

        Task<DateTime?> GetLastSynchronisationAsync(string rowKey);

        Task<Dictionary<string, string>> GetLastTokensAsync(string rowKey);

        Task<bool?> GetIsSynchronisationRunningAsync(string rowKey);

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

        public async Task<DateTime?> GetLastSynchronisationAsync(string rowKey)
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return null;

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, rowKey, settingsTable);
            if (currentSetting == null) return null;
            return currentSetting.LastSynchronisationChanged;
        }

        public async Task<Dictionary<string, string>> GetLastTokensAsync(string rowKey)
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return new Dictionary<string, string>();

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, rowKey, settingsTable);
            if (currentSetting == null) return new Dictionary<string, string>();
            return currentSetting.LastTokenForDeleted.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(part => part.Split('='))
               .ToDictionary(split => split[0], split => split[1]);
        }

        public async Task<bool?> GetIsSynchronisationRunningAsync(string rowKey)
        {
            var settingsTable = GetSettingsTable();
            if (settingsTable == null) return null;

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, rowKey, settingsTable);
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
                    // Create blob for acquiring lease.
                    var blob = leaseContainer.GetBlockBlobReference(String.Format("{0}.lck", _globalSettings.SettingsPartitionKey));
                    await blob.UploadTextAsync("");

                    // Acquire lease.
                    var leaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(30), Guid.NewGuid().ToString());
                    if (string.IsNullOrEmpty(leaseId))
                    {
                        throw new AcquiringLeaseException("Error while acquiring lease");
                    }
                    try
                    {
                        // Get new sequence number after acquiring lease.
                        setting.SequenceNumber = await GetSequenceNumberAsync() + 1;
                        await _storageTableService.SaveAsync(settingsTable, setting);
                    }
                    finally
                    {
                        await blob.ReleaseLeaseAsync(AccessCondition.GenerateLeaseCondition(leaseId));
                    }
                    return setting.SequenceNumber;
                }
                catch (AcquiringLeaseException ex)
                {
                    // Lease is still active, continue loop.
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
