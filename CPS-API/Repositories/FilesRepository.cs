using CPS_API.Models;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;

namespace CPS_API.Repositories
{
    public interface IFilesRepository
    {
        Task<CpsFile> GetFileAsync(string contentId);

        Task<string?> GetUrlAsync(string contentId);

        Task<string?> CreateFileAsync(HttpRequest Request, CpsFile file);

        Task<ContentIds> CreateFileAsync(CpsFile file);

        Task<DriveItem?> PutFileAsync(string siteId, string fileName, MemoryStream stream);

        Task<bool> UpdateContentAsync(HttpRequest Request, string contentId, byte[] content);

        Task DeleteFileAsync(string siteId, string driveItemId);

        Task<FileInformation> GetMetadataAsync(string contentId);

        Task<ListItem?> UpdateMetadataAsync(FileInformation metadata);
    }

    public class FilesRepository : IFilesRepository
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IContentIdRepository _contentIdRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IDriveRepository _driveRepository;

        public FilesRepository(GraphServiceClient graphClient, IContentIdRepository contentIdRepository, Microsoft.Extensions.Options.IOptions<GlobalSettings> settings, IDriveRepository driveRepository)
        {
            _graphClient = graphClient;
            _contentIdRepository = contentIdRepository;
            _globalSettings = settings.Value;
            _driveRepository = driveRepository;
        }

        public Task<ContentIds> CreateFileAsync(CpsFile file)
        {
            // Create new file in fileStorage with filename + content in specified location (using site/web/list or drive ids)
            // Find all missing ids and return them

            throw new NotImplementedException();
        }

        public async Task<string?> GetUrlAsync(string contentId)
        {
            DocumentIdsEntity? sharepointIds;
            try
            {
                sharepointIds = await _contentIdRepository.GetSharepointIdsAsync(contentId);
            }
            catch (Exception ex) when (ex.InnerException is not UnauthorizedAccessException && ex is not FileNotFoundException)
            {
                throw new Exception("Error while getting sharePointIds");
            }
            if (sharepointIds == null) throw new FileNotFoundException($"SharepointIds (contentId = {contentId}) does not exist!");

            DriveItem? driveItem;
            try
            {
                driveItem = await _driveRepository.GetDriveItemAsync(sharepointIds.SiteId, sharepointIds.ListId, sharepointIds.ListItemId);
            }
            catch (Exception ex) when (ex.InnerException is not UnauthorizedAccessException)
            {
                throw new Exception("Error while getting driveItem");
            }
            if (driveItem == null) throw new FileNotFoundException($"DriveItem (contentId = {contentId}) does not exist!");

            // Get url
            return driveItem.WebUrl;
        }

        public async Task<CpsFile> GetFileAsync(string contentId)
        {
            FileInformation metadata;
            try
            {
                metadata = await GetMetadataAsync(contentId);
            }
            catch (Exception)
            {
                throw new Exception("Error while getting metadata");
            }
            if (metadata == null) throw new FileNotFoundException($"Metadata (contentId = {contentId}) does not exist!");

            return new CpsFile
            {
                Metadata = metadata
            };
        }

        public async Task<string?> CreateFileAsync(HttpRequest Request, CpsFile file)
        {
            // Save content temporary in App Service memory.
            // Failed? Log error in App Insights

            // Add new file in SharePoint
            DriveItem? driveItem;
            try
            {
                driveItem = await PutFileAsync(Request, file);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                // TODO: Log error in App Insights
                throw ex;
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights
                throw new Exception("Error while adding new file");
            }
            if (driveItem == null)
            {
                // TODO: Log error in App Insights
                throw new Exception("Error while adding new file");
            }

            // Get sharepointIds
            ContentIds sharepointIds;
            try
            {
                sharepointIds = await GetSharepointIdsAsync(file, driveItem);
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await DeleteFileAsync(file.Metadata.Ids.SiteId, driveItem.Id);

                throw new Exception("Error while getting SharepointIds");
            }

            // Generate contentId
            string? contentId;
            try
            {
                contentId = await _contentIdRepository.GenerateContentIdAsync(sharepointIds);
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await DeleteFileAsync(file.Metadata.Ids.SiteId, driveItem.Id);

                throw new Exception("Error while generating contentId");
            }
            if (contentId.IsNullOrEmpty())
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await DeleteFileAsync(file.Metadata.Ids.SiteId, driveItem.Id);

                throw new Exception("Error while generating contentId");
            }

            // Update ContentId and metadata in Sharepoint with Graph
            try
            {
                await UpdateMetadataAsync(file.Metadata);
            }
            catch (Exception ex)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await DeleteFileAsync(file.Metadata.Ids.SiteId, driveItem.Id);

                throw new Exception("Error while updating metadata");
            }

            // Done
            return contentId;
        }

        private async Task<DriveItem?> PutFileAsync(HttpRequest Request, CpsFile file)
        {
            using (var ms = new MemoryStream())
            {
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                return await PutFileAsync(file.Metadata.Ids.SiteId, file.Metadata.FileName, ms);
            }
        }

        private async Task<ContentIds?> GetSharepointIdsAsync(CpsFile file, DriveItem driveItem)
        {
            var sharepointIds = file.Metadata.Ids;
            sharepointIds.DriveItemId = driveItem.Id;
            if (driveItem.SharepointIds == null)
            {
                var driveItemWithIds = await _graphClient.Drives[file.Metadata.Ids.DriveId].Items[driveItem.Id].Request().Select("sharepointids").GetAsync();
                if (driveItemWithIds == null) return null;
                driveItem.SharepointIds = driveItemWithIds.SharepointIds;
            }
            sharepointIds.WebId = driveItem.SharepointIds.WebId;
            sharepointIds.ListId = driveItem.SharepointIds.ListId;
            sharepointIds.ListItemId = driveItem.SharepointIds.ListItemId;
            return sharepointIds;
        }

        public async Task<DriveItem?> PutFileAsync(string siteId, string fileName, MemoryStream stream)
        {
            return await _graphClient.Sites[siteId].Drive.Root.ItemWithPath(fileName).Content.Request().PutAsync<DriveItem>(stream);
        }

        public async Task<bool> UpdateContentAsync(HttpRequest Request, string contentId, byte[] content)
        {
            // Get SharepointIds
            DocumentIdsEntity? sharepointIds;
            try
            {
                sharepointIds = await _contentIdRepository.GetSharepointIdsAsync(contentId);
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                throw new Exception("Error while getting SharepointIds");
            }
            if (sharepointIds == null) throw new FileNotFoundException("SharepointIds not found");

            // Get DriveItem
            DriveItem? driveItem;
            try
            {
                driveItem = await _driveRepository.GetDriveItemAsync(sharepointIds.SiteId, sharepointIds.ListId, sharepointIds.ListItemId);
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                throw new Exception("Error while getting DriveItem");
            }
            if (driveItem == null) throw new FileNotFoundException("DriveItem not found");

            // Create new version
            try
            {
                using (var ms = new MemoryStream())
                {
                    await Request.Body.CopyToAsync(ms);
                    ms.Position = 0;

                    await _graphClient.Me.Drive.Items[driveItem.Id].Request().UpdateAsync(driveItem);
                }
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                throw new Exception("Error while updating DriveItem");
            }

            return true;
        }

        public async Task DeleteFileAsync(string siteId, string driveItemId)
        {
            await _graphClient.Sites[siteId].Drive.Items[driveItemId].Request().DeleteAsync();
        }

        #region Metadata

        public async Task<FileInformation> GetMetadataAsync(string contentId)
        {
            ListItem? file;
            try
            {
                file = await getListItem(contentId);
            }
            catch (Exception)
            {
                throw new Exception("Error while getting listItem");
            }
            if (file == null) throw new FileNotFoundException($"LisItem (contentId = {contentId}) does not exist!");

            DriveItem? driveItem;
            try
            {
                driveItem = await getDriveItem(contentId);
            }
            catch (Exception)
            {
                throw new Exception("Error while getting driveItem");
            }
            if (driveItem == null) throw new FileNotFoundException($"DriveItem (contentId = {contentId}) does not exist!");

            var fileName = file.Name.IsNullOrEmpty() ? driveItem.Name : file.Name;

            FileInformation metadata = new FileInformation();
            metadata.MimeType = driveItem.File?.MimeType ?? string.Empty;
            metadata.FileName = fileName;
            metadata.AdditionalMetadata = new FileMetadata();

            if (!fileName.IsNullOrEmpty() && fileName.Contains('.'))
                metadata.FileExtension = fileName.Split('.')[1];

            if (file.CreatedDateTime.HasValue)
                metadata.CreatedOn = file.CreatedDateTime.Value.DateTime;

            if (file.CreatedBy.User != null)
                metadata.CreatedBy = file.CreatedBy.User.DisplayName;
            else if (file.CreatedBy.Application != null)
                metadata.CreatedBy = file.CreatedBy.Application.DisplayName;

            if (file.LastModifiedDateTime.HasValue)
                metadata.ModifiedOn = file.LastModifiedDateTime.Value.DateTime;

            if (file.LastModifiedBy.User != null)
                metadata.ModifiedBy = file.LastModifiedBy.User.DisplayName;
            else if (file.LastModifiedBy.Application != null)
                metadata.ModifiedBy = file.LastModifiedBy.Application.DisplayName;

            foreach (var fieldMapping in _globalSettings.MetadataSettings)
            {
                // create object with sharepoint fields metadata + url to item
                file.Fields.AdditionalData.TryGetValue(fieldMapping.SpoColumnName, out var value);
                if (value == null)
                {
                    // log warning to insights?
                    // TODO: If the property has no value in Sharepoint then the column is not present in AdditionalData, how do we handle this?
                }
                else
                {
                    var stringValue = value.ToString();
                    if (fieldMapping.FieldName.Equals(nameof(FileMetadata.Classification), StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (stringValue != null)
                        {
                            Enum.TryParse<Classification>(stringValue, out var enumValue);
                            metadata.AdditionalMetadata[fieldMapping.FieldName] = enumValue;
                        }
                    }
                    else if (fieldMapping.FieldName.Equals(nameof(FileMetadata.RetentionPeriod), StringComparison.InvariantCultureIgnoreCase))
                    {
                        var decimalValue = Convert.ToDecimal(stringValue, new CultureInfo("en-US"));
                        if (decimalValue % 1 == 0)
                        {
                            metadata.AdditionalMetadata[fieldMapping.FieldName] = (int)decimalValue;
                        }
                    }
                    else if (
                        fieldMapping.FieldName.Equals(nameof(FileMetadata.PublicationDate), StringComparison.InvariantCultureIgnoreCase)
                        || fieldMapping.FieldName.Equals(nameof(FileMetadata.ArchiveDate), StringComparison.InvariantCultureIgnoreCase))
                    {
                        DateTime.TryParse(stringValue, out var dateValue);
                        metadata.AdditionalMetadata[fieldMapping.FieldName] = dateValue;
                    }
                    else
                    {
                        metadata.AdditionalMetadata[fieldMapping.FieldName] = stringValue;
                    }
                }
            }

            var sharepointIds = await _contentIdRepository.GetSharepointIdsAsync(contentId);
            if (sharepointIds == null)
            {
                throw new Exception("Error while getting sharepointIds");
            }

            metadata.Ids = new ContentIds(sharepointIds);
            return metadata;
        }

        public async Task<ListItem?> UpdateMetadataAsync(FileInformation metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            ListItem? listItem = await getListItem(metadata.Ids.ContentId);
            if (listItem == null) throw new FileNotFoundException();

            // map received metadata to SPO object
            mapMetadata(metadata, ref listItem);

            // update sharepoint fields with metadata
            return await _graphClient.Sites[metadata.Ids.SiteId].Lists[metadata.Ids.ListId].Items[metadata.Ids.ListItemId].Request().PutAsync(listItem);
        }

        private void mapMetadata(FileInformation metadata, ref ListItem listItem)
        {
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException("metadata.AdditionalMetadata");

            foreach (var fieldMapping in _globalSettings.MetadataSettings)
            {
                try
                {
                    var value = metadata.AdditionalMetadata[fieldMapping.FieldName];
                    listItem.Fields.AdditionalData[fieldMapping.SpoColumnName] = value;
                }
                catch
                {
                    throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
                }
            }
        }

        #endregion

        #region Helpers

        private async Task<ListItem?> getListItem(string contentId)
        {
            // Find file info in documents table by contentid
            var sharepointIds = await _contentIdRepository.GetSharepointIdsAsync(contentId);
            if (sharepointIds == null) throw new FileNotFoundException($"SharepointIds (contentId = {contentId}) does not exist!");

            // Find file in SharePoint using ids
            var queryOptions = new List<QueryOption>()
            {
                new QueryOption("expand", "fields")
            };

            return await _graphClient.Sites[sharepointIds.SiteId].Lists[sharepointIds.ListId].Items[sharepointIds.ListItemId].Request(queryOptions).GetAsync();
        }

        private async Task<DriveItem?> getDriveItem(string contentId)
        {
            // Find file info in documents table by contentid
            var sharepointIds = await _contentIdRepository.GetSharepointIdsAsync(contentId);
            if (sharepointIds == null) throw new FileNotFoundException($"SharepointIds (contentId = {contentId}) does not exist!");

            return await _driveRepository.GetDriveItemAsync(sharepointIds.SiteId, sharepointIds.ListId, sharepointIds.ListItemId);
        }

        #endregion
    }
}