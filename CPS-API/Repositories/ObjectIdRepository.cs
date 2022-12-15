using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.IdentityModel.Tokens;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IObjectIdRepository
    {
        Task<string> GenerateObjectIdAsync(ObjectIds sharePointIds);

        Task<DocumentIdsEntity?> GetSharepointIdsAsync(string objectId);

        Task<string?> GetObjectIdAsync(ObjectIds sharePointIds);

        Task<bool> SaveObjectIdsAsync(string objectId, ObjectIds objectIds);
    }

    public class ObjectIdRepository : IObjectIdRepository
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly StorageTableService _storageTableService;
        private readonly IDriveRepository _driveRepository;

        public ObjectIdRepository(ISettingsRepository settingsRepository,
                                   StorageTableService storageTableService,
                                   IDriveRepository driveRepository)
        {
            _settingsRepository = settingsRepository;
            _storageTableService = storageTableService;
            _driveRepository = driveRepository;
        }

        public async Task<string> GenerateObjectIdAsync(ObjectIds sharePointIds)
        {
            // Check if sharepointIds already in table, if so; return objectId.
            var existingObjectId = await GetObjectIdAsync(sharePointIds);
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
            sharePointIds.ObjectId = objectId;

            // Add any missing location IDs
            sharePointIds = await FindMissingIds(sharePointIds);

            // Store objectId + backend ids in table
            try
            {
                succeeded = await SaveObjectIdsAsync(objectId, sharePointIds);
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

        private async Task<ObjectIds> FindMissingIds(ObjectIds sharePointIds)
        {
            if (sharePointIds.DriveId.IsNullOrEmpty() || sharePointIds.DriveItemId.IsNullOrEmpty())
            {
                // Find driveID + driveItemID for object
                try
                {
                    var drive = await _driveRepository.GetDriveAsync(sharePointIds.SiteId, sharePointIds.ListId);
                    sharePointIds.DriveId = drive.Id;
                    var driveItem = await _driveRepository.GetDriveItemAsync(sharePointIds.SiteId, sharePointIds.ListId, sharePointIds.ListItemId);
                    sharePointIds.DriveItemId = driveItem.Id;
                }
                catch (Exception ex)
                {
                    throw new Exception("Error while getting driveId + driveItemId", ex);
                }
            }

            if (sharePointIds.SiteId.IsNullOrEmpty() || sharePointIds.ListId.IsNullOrEmpty() || sharePointIds.ListItemId.IsNullOrEmpty())
            {
                // Find sharepoint Ids from drive
                try
                {
                    var driveItem = await _driveRepository.GetDriveItemIdsAsync(sharePointIds.DriveId, sharePointIds.DriveItemId);
                    sharePointIds.SiteId = driveItem.SharepointIds.SiteId;
                    sharePointIds.ListId = driveItem.SharepointIds.ListId;
                    sharePointIds.ListItemId = driveItem.SharepointIds.ListItemId;
                }
                catch (Exception ex)
                {
                    throw new Exception("Error while getting SharePoint Ids", ex);
                }
            }

            return sharePointIds;
        }

        private CloudTable? GetDocumentIdsTable()
        {
            return _storageTableService.GetTable(Helpers.Constants.DocumentIdsTableName);
        }

        public async Task<DocumentIdsEntity?> GetSharepointIdsAsync(string objectId)
        {
            var documentIdsEntity = await GetDocumentIdsEntityAsync(objectId);
            if (documentIdsEntity == null) throw new FileNotFoundException($"DocumentIdsEntity (objectId = {objectId}) does not exist!");

            return documentIdsEntity;
        }

        private async Task<DocumentIdsEntity?> GetDocumentIdsEntityAsync(string objectId)
        {
            var documentIdsTable = GetDocumentIdsTable();
            if (documentIdsTable == null)
            {
                return null;
            }

            var filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, objectId);
            var query = new TableQuery<DocumentIdsEntity>().Where(filter);

            var result = await documentIdsTable.ExecuteQuerySegmentedAsync(query, null);
            return result.Results?.FirstOrDefault();
        }

        public async Task<string?> GetObjectIdAsync(ObjectIds sharePointIds)
        {
            var documentIdsTable = GetDocumentIdsTable();
            if (documentIdsTable == null)
            {
                return null;
            }

            var rowKey = sharePointIds.SiteId + sharePointIds.ListId + sharePointIds.ListItemId;
            var filter = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, rowKey);
            var query = new TableQuery<DocumentIdsEntity>().Where(filter);

            var result = await documentIdsTable.ExecuteQuerySegmentedAsync(query, null);
            var documentIdsEntity = result.Results?.FirstOrDefault();
            return documentIdsEntity?.PartitionKey;
        }

        public async Task<bool> SaveObjectIdsAsync(string objectId, ObjectIds objectIds)
        {
            var documentIdsTable = GetDocumentIdsTable();
            if (documentIdsTable == null)
            {
                return false;
            }

            var document = new DocumentIdsEntity(objectId, objectIds);
            await _storageTableService.SaveAsync(documentIdsTable, document);
            return true;
        }
    }
}