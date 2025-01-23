using System.Diagnostics;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace CPS_API.Repositories
{
    public interface ISettingsRepository
    {
        Task<T?> GetSetting<T>(string fieldName);

        Task<Dictionary<string, string>> GetLastTokensAsync(string fieldName);

        Task<bool> SaveSettingAsync(string fieldName, object? value);

        Task<long?> IncreaseSequenceNumberAsync();
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

        private async Task<SettingsEntity?> GetCurrentSettings()
        {
            var table = _storageTableService.GetTable(_globalSettings.SettingsTableName);
            if (table == null)
            {
                throw new CpsException($"Table \"{_globalSettings.SettingsTableName}\" not found");
            }

            var currentSetting = await _storageTableService.GetAsync<SettingsEntity>(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsRowKey, table);
            if (currentSetting == null) currentSetting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsRowKey);
            return currentSetting;
        }

        public async Task<T?> GetSetting<T>(string fieldName)
        {
            var currentSetting = await GetCurrentSettings();
            return FieldPropertyHelper.GetFieldValue<T>(currentSetting, fieldName);
        }

        public async Task<Dictionary<string, string>> GetLastTokensAsync(string fieldName)
        {
            var value = await GetSetting<string>(fieldName);
            if (string.IsNullOrEmpty(value)) return new Dictionary<string, string>();
            return value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(part => part.Split('='))
               .ToDictionary(split => split[0], split => split.Length == 2 ? split[1] : split[1] + "=" + split[2]);
        }

        public async Task<bool> SaveSettingAsync(string fieldName, object? value)
        {
            // Set up lease container
            var leaseContainer = await _storageTableService.GetLeaseContainer();
            if (leaseContainer == null)
            {
                throw new CpsException("Error while getting leaseContainer");
            }

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

                    // Actually update settings
                    try
                    {
                        var settings = await GetCurrentSettings();
                        FieldPropertyHelper.SetFieldValue(settings, fieldName, value);
                        await SaveSettingsAsync(settings);
                    }
                    finally
                    {
                        await blob.ReleaseLeaseAsync(AccessCondition.GenerateLeaseCondition(leaseId));
                    }
                    return true;
                }
                catch (AcquiringLeaseException)
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
            return false;
        }

        private async Task<bool> SaveSettingsAsync(SettingsEntity setting)
        {
            var settingsTable = _storageTableService.GetTable(_globalSettings.SettingsTableName);
            if (settingsTable == null)
            {
                throw new CpsException($"Table \"{_globalSettings.SettingsTableName}\" not found");
            }

            await _storageTableService.SaveAsync(settingsTable, setting);
            return true;
        }

        public async Task<long?> IncreaseSequenceNumberAsync()
        {
            var leaseContainer = await _storageTableService.GetLeaseContainer();
            if (leaseContainer == null)
            {
                throw new CpsException("Error while getting leaseContainer");
            }

            // Try to get the new sequence number and saving it in the storage table.
            var s = new Stopwatch();
            s.Start();
            while (s.Elapsed < TimeSpan.FromSeconds(30))
            {
                try
                {
                    (CloudBlockBlob blob, string leaseId) = await GetLeaseId(leaseContainer);

                    // Get new sequence number after acquiring lease.
                    var settings = await GetCurrentSettings();
                    if (settings == null) throw new CpsException("Error while getting settings");
                    try
                    {
                        if (!settings.SequenceNumber.HasValue) settings.SequenceNumber = 1;
                        else settings.SequenceNumber++;

                        await SaveSettingsAsync(settings);
                    }
                    finally
                    {
                        await blob.ReleaseLeaseAsync(AccessCondition.GenerateLeaseCondition(leaseId));
                    }
                    return settings.SequenceNumber;
                }
                catch (AcquiringLeaseException)
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

        private async Task<(CloudBlockBlob, string)> GetLeaseId(CloudBlobContainer leaseContainer)
        {
            // Create blob for acquiring lease.
            var blob = leaseContainer.GetBlockBlobReference(String.Format("{0}.lck", _globalSettings.SettingsPartitionKey));
            await blob.UploadTextAsync("");

            // Acquire lease.
            var leaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(30), Guid.NewGuid().ToString());
            if (string.IsNullOrEmpty(leaseId)) throw new AcquiringLeaseException("Error while acquiring lease");
            return (blob, leaseId);
        }
    }
}