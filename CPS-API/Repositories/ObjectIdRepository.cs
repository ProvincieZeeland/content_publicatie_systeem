﻿using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IObjectIdRepository
    {
        Task<string> GenerateObjectIdAsync(ObjectIdentifiers ids);

        Task<ObjectIdentifiersEntity?> GetObjectIdentifiersAsync(string objectId);

        Task<string?> GetObjectIdAsync(ObjectIdentifiers ids);

        Task<bool> SaveObjectIdentifiersAsync(string objectId, ObjectIdentifiers ids);

        Task<ObjectIdentifiers> FindMissingIds(ObjectIdentifiers ids);
    }

    public class ObjectIdRepository : IObjectIdRepository
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly StorageTableService _storageTableService;
        private readonly IDriveRepository _driveRepository;
        private readonly GlobalSettings _globalSettings;

        public ObjectIdRepository(ISettingsRepository settingsRepository,
                                   StorageTableService storageTableService,
                                   IDriveRepository driveRepository,
                                   IOptions<GlobalSettings> settings)
        {
            _settingsRepository = settingsRepository;
            _storageTableService = storageTableService;
            _driveRepository = driveRepository;
            _globalSettings = settings.Value;
        }

        public async Task<string> GenerateObjectIdAsync(ObjectIdentifiers ids)
        {
            // Add any missing location IDs before looking for existing.
            ids = await FindMissingIds(ids);

            // Check if objectIdentifiers already in table, if so; return objectId.
            var existingObjectId = await GetObjectIdAsync(ids);
            if (existingObjectId != null)
            {
                return existingObjectId;
            }

            // Get sequencenr for objectId from table
            var currentSequenceNumber = await _settingsRepository.GetSequenceNumberAsync();
            if (currentSequenceNumber == null)
            {
                throw new Exception("Current sequence not found");
            }

            // Increase sequencenr and store in table
            var sequence = currentSequenceNumber.Value + 1;
            var newSetting = new SettingsEntity(sequence);
            bool succeeded;
            try
            {
                succeeded = await _settingsRepository.SaveSettingAsync(newSetting);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error while saving new sequence {sequence}");
            }
            if (!succeeded)
            {
                throw new Exception($"Error while saving new sequence {sequence}");
            }

            // Create new objectId
            var objectId = $"ZLD{DateTime.Now.Year}-{sequence}";
            ids.ObjectId = objectId;

            // Store objectId + backend ids in table
            try
            {
                succeeded = await SaveObjectIdentifiersAsync(objectId, ids);
                if (!succeeded)
                {
                    throw new Exception("Error while saving SharePoint ids");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error while saving SharePoint ids", ex);
            }

            return objectId;
        }

        public async Task<ObjectIdentifiers> FindMissingIds(ObjectIdentifiers ids)
        {
            if (ids.DriveId.IsNullOrEmpty() || ids.DriveItemId.IsNullOrEmpty())
            {
                // Find driveID + driveItemID for object
                try
                {
                    var drive = await _driveRepository.GetDriveAsync(ids.SiteId, ids.ListId);
                    ids.DriveId = drive.Id;
                    var driveItem = await _driveRepository.GetDriveItemAsync(ids.SiteId, ids.ListId, ids.ListItemId);
                    ids.DriveItemId = driveItem.Id;
                }
                catch (Exception ex)
                {
                    throw new Exception("Error while getting driveId + driveItemId", ex);
                }
            }

            if (ids.SiteId.IsNullOrEmpty() || ids.ListId.IsNullOrEmpty() || ids.ListItemId.IsNullOrEmpty())
            {
                // Find sharepoint Ids from drive
                try
                {
                    var driveItem = await _driveRepository.GetDriveItemIdsAsync(ids.DriveId, ids.DriveItemId);
                    ids.SiteId = driveItem.SharepointIds.SiteId;
                    ids.ListId = driveItem.SharepointIds.ListId;
                    ids.ListItemId = driveItem.SharepointIds.ListItemId;
                }
                catch (Exception ex)
                {
                    throw new Exception("Error while getting SharePoint Ids", ex);
                }
            }

            if (ids.ExternalReferenceListId.IsNullOrEmpty())
            {
                var locationMapping = _globalSettings.LocationMapping.FirstOrDefault(item =>
                                        item.SiteId == ids.SiteId
                                        && item.ListId == ids.ListId
                                      );
                ids.ExternalReferenceListId = locationMapping?.ExternalReferenceListId;
            }

            return ids;
        }

        private CloudTable? GetObjectIdentifiersTable()
        {
            return _storageTableService.GetTable(Helpers.Constants.ObjectIdentifiersTableName);
        }

        public async Task<ObjectIdentifiersEntity?> GetObjectIdentifiersAsync(string objectId)
        {
            var objectIdentifiersEntity = await GetObjectIdentifiersEntityAsync(objectId);
            if (objectIdentifiersEntity == null) throw new FileNotFoundException($"ObjectIdentifiersEntity (objectId = {objectId}) does not exist!");

            return objectIdentifiersEntity;
        }

        private async Task<ObjectIdentifiersEntity?> GetObjectIdentifiersEntityAsync(string objectId)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();
            if (objectIdentifiersTable == null)
            {
                return null;
            }

            var filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, objectId);
            var query = new TableQuery<ObjectIdentifiersEntity>().Where(filter);

            var result = await objectIdentifiersTable.ExecuteQuerySegmentedAsync(query, null);
            return result.Results?.FirstOrDefault();
        }

        public async Task<string?> GetObjectIdAsync(ObjectIdentifiers ids)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();
            if (objectIdentifiersTable == null)
            {
                return null;
            }

            var rowKey = ids.SiteId + ids.ListId + ids.ListItemId;
            var filter = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, rowKey);
            var query = new TableQuery<ObjectIdentifiersEntity>().Where(filter);

            var result = await objectIdentifiersTable.ExecuteQuerySegmentedAsync(query, null);
            var objectIdentifiersEntity = result.Results?.FirstOrDefault();
            return objectIdentifiersEntity?.PartitionKey;
        }

        public async Task<bool> SaveObjectIdentifiersAsync(string objectId, ObjectIdentifiers ids)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();
            if (objectIdentifiersTable == null)
            {
                return false;
            }

            var document = new ObjectIdentifiersEntity(objectId, ids);
            await _storageTableService.SaveAsync(objectIdentifiersTable, document);
            return true;
        }
    }
}