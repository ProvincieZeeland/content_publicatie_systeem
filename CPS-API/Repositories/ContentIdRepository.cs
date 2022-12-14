using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IContentIdRepository
    {
        Task<string> GenerateContentIdAsync(ContentIds sharePointIds);

        Task<DocumentIdsEntity?> GetSharepointIdsAsync(string contentId);

        Task<string?> GetContentIdAsync(ContentIds sharePointIds);

        Task<bool> SaveContentIdsAsync(string contentId, ContentIds contentIds);
    }

    public class ContentIdRepository : IContentIdRepository
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly StorageTableService _storageTableService;
        private readonly IDriveRepository _driveRepository;

        public ContentIdRepository(ISettingsRepository settingsRepository,
                                   StorageTableService storageTableService,
                                   IDriveRepository driveRepository)
        {
            _settingsRepository = settingsRepository;
            _storageTableService = storageTableService;
            _driveRepository = driveRepository;
        }

        public async Task<string> GenerateContentIdAsync(ContentIds sharePointIds)
        {
            // Check if sharepointIds already in table, if so; return contentId.
            var existingContentId = await GetContentIdAsync(sharePointIds);
            if (existingContentId != null)
            {
                return existingContentId;
            }

            // Get sequencenr for contentid from table
            var currentSequenceNumber = await _settingsRepository.GetSequenceNumberAsync();
            if (currentSequenceNumber == null)
            {
                throw new Exception("Current sequence not found");
            }

            // Increase sequencenr and store in table
            var sequence = currentSequenceNumber.Value + 1;
            var newSetting = new SettingsEntity(sequence);
            var succeeded = await _settingsRepository.SaveSettingAsync(newSetting);
            if (!succeeded)
            {
                throw new Exception($"Error while saving new sequence {sequence}");
            }

            // Create new contentId
            var contentId = $"ZLD{DateTime.Now.Year}-{sequence}";
            sharePointIds.ContentId = contentId;

            // Add any missing location IDs
            sharePointIds = await FindMissingIds(sharePointIds);

            // Store contentId + backend ids in table
            try
            {
                succeeded = await SaveContentIdsAsync(contentId, sharePointIds);
                if (!succeeded)
                {
                    throw new Exception("Error while saving SharePoint ids");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error while saving SharePoint ids", ex);
            }

            return contentId;
        }

        private async Task<ContentIds> FindMissingIds(ContentIds sharePointIds)
        {
            if (sharePointIds.DriveId == null || sharePointIds.DriveItemId == null)
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
            else if (sharePointIds.SiteId == null || sharePointIds.ListId == null || sharePointIds.ListItemId == null)
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

        public async Task<DocumentIdsEntity?> GetSharepointIdsAsync(string contentId)
        {
            var documentIdsEntity = await GetDocumentIdsEntityAsync(contentId);
            if (documentIdsEntity == null) throw new FileNotFoundException($"DocumentIdsEntity (contentId = {contentId}) does not exist!");

            return documentIdsEntity;
        }

        private async Task<DocumentIdsEntity?> GetDocumentIdsEntityAsync(string contentId)
        {
            var documentIdsTable = GetDocumentIdsTable();
            if (documentIdsTable == null)
            {
                return null;
            }

            var filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, contentId);
            var query = new TableQuery<DocumentIdsEntity>().Where(filter);

            var result = await documentIdsTable.ExecuteQuerySegmentedAsync(query, null);
            return result.Results?.FirstOrDefault();
        }

        public async Task<string?> GetContentIdAsync(ContentIds sharePointIds)
        {
            var documentIdsTable = GetDocumentIdsTable();
            if (documentIdsTable == null)
            {
                return null;
            }

            var rowKey = sharePointIds.SiteId + sharePointIds.WebId + sharePointIds.ListId + sharePointIds.ListItemId;
            var filter = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, rowKey);
            var query = new TableQuery<DocumentIdsEntity>().Where(filter);

            var result = await documentIdsTable.ExecuteQuerySegmentedAsync(query, null);
            var documentIdsEntity = result.Results?.FirstOrDefault();
            return documentIdsEntity?.PartitionKey;
        }

        public async Task<bool> SaveContentIdsAsync(string contentId, ContentIds contentIds)
        {
            var documentIdsTable = GetDocumentIdsTable();
            if (documentIdsTable == null)
            {
                return false;
            }

            var document = new DocumentIdsEntity(contentId, contentIds);
            await _storageTableService.SaveAsync(documentIdsTable, document);
            return true;
        }
    }
}