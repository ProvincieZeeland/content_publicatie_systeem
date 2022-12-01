using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.Graph;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IContentIdRepository
    {
        Task<string> GenerateContentIdAsync(ContentIds sharePointIds);

        Task<DocumentIdsEntity?> GetSharePointIdsAsync(string contentId);

        Task<string?> GetContentIdAsync(ContentIds sharePointIds);

        Task<bool> SaveContentIdsAsync(string contentId, ContentIds contentIds);
    }

    public class ContentIdRepository : IContentIdRepository
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly StorageTableService _storageTableService;
        private readonly GraphServiceClient _graphClient;

        public ContentIdRepository(ISettingsRepository settingsRepository,
                                   StorageTableService storageTableService,
                                   GraphServiceClient graphClient)
        {
            _settingsRepository = settingsRepository;
            _storageTableService = storageTableService;
            _graphClient = graphClient;
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
            var currentSetting = await _settingsRepository.GetCurrentSettingAsync();
            if (currentSetting == null || currentSetting.SequenceNumber < 0)
            {
                throw new Exception("Current sequence not found");
            }

            // Increase sequencenr and store in table
            var sequence = currentSetting.SequenceNumber + 1;
            var newSetting = new SettingsEntity(sequence);
            var succeeded = await _settingsRepository.SaveSettingAsync(newSetting);
            if (!succeeded)
            {
                throw new Exception($"Error while saving new sequence {sequence}");
            }

            // Create new contentId
            var contentId = $"ZLD{DateTime.Now.Year}-{sequence}";

            // Find driveID + driveItemID for object
            try
            {
                var drive = await _graphClient.Sites[sharePointIds.SiteId].Drive.Request().GetAsync();
                sharePointIds.DriveId = drive.Id;
                var driveItem = await _graphClient.Sites[sharePointIds.SiteId].Lists[sharePointIds.ListId].Items[sharePointIds.ListItemId].DriveItem.Request().GetAsync();
                sharePointIds.DriveItemId = driveItem.Id;
            }
            catch (Exception)
            {
                throw new Exception("Error while getting driveId + driveItemId");
            }

            // Store contentId + backend ids in table
            try
            {
                succeeded = await SaveContentIdsAsync(contentId, sharePointIds);
                if (!succeeded)
                {
                    throw new Exception("Error while saving SharePoint ids");
                }
            }
            catch (Exception)
            {
                throw new Exception("Error while saving SharePoint ids");
            }

            return contentId;
        }

        private CloudTable? GetDocumentIdsTable()
        {
            return _storageTableService.GetTable(Helpers.Constants.DocumentIdsTableName);
        }

        public async Task<DocumentIdsEntity?> GetSharePointIdsAsync(string contentId)
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
