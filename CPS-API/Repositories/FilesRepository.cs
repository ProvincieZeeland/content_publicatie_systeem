using CPS_API.Models;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;

namespace CPS_API.Repositories
{
    public interface IFilesRepository
    {
        Task<CpsFile> GetFileAsync(string contentId);

        Task<string> GetUrlAsync(string contentId);

        Task<ContentIds> CreateFileAsync(CpsFile file);

        Task<ContentIds> CreateFileAsync(CpsFile file, IFormFile formFile);

        Task<bool> UpdateContentAsync(HttpRequest Request, string contentId, byte[] content);

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

        public async Task<string> GetUrlAsync(string contentId)
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

        public async Task<ContentIds> CreateFileAsync(CpsFile file)
        {
            return await CreateFileAsync(file, null);
        }

        public async Task<ContentIds> CreateFileAsync(CpsFile file, IFormFile formFile)
        {
            // Find wanted storage location depending on classification
            // todo: get driveid or site matching classification & source
            ContentIds locationIds = new ContentIds
            {
                DriveId = ""
            };

            // Add new file to SharePoint
            DriveItem driveItem;
            try
            {
                if (formFile != null)
                {
                    using (var fileStream = formFile.OpenReadStream())
                    {
                        if (fileStream.Length > 0)
                        {
                            driveItem = await _driveRepository.CreateAsync(locationIds.DriveId, file.Metadata.FileName, fileStream);
                        }
                        else
                        {
                            throw new Exception("File cannot be empty");
                        }
                    }
                }
                else
                {
                    using (var memorstream = new MemoryStream(file.Content))
                    {
                        if (memorstream.Length > 0)
                        {
                            memorstream.Position = 0;
                            driveItem = await _driveRepository.CreateAsync(locationIds.DriveId, file.Metadata.FileName, memorstream);
                        }
                        else
                        {
                            throw new Exception("File cannot be empty");
                        }
                    }
                }

                if (driveItem == null)
                {
                    throw new Exception("Error while adding new file");
                }

                locationIds.DriveItemId = driveItem.Id;
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                // TODO: Log error in App Insights
                throw;
            }
            // todo: handle file exists exception
            catch (Exception)
            {
                // TODO: Log error in App Insights
                throw new Exception("Error while adding new file");
            }

            // Generate contentId
            string contentId;
            try
            {
                contentId = await _contentIdRepository.GenerateContentIdAsync(locationIds);
                if (contentId.IsNullOrEmpty()) throw new Exception("ContentID is empty");

                locationIds.ContentId = contentId;
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(locationIds.DriveId, driveItem.Id);

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
                await _driveRepository.DeleteFileAsync(locationIds.DriveId, driveItem.Id);

                throw new Exception("Error while updating metadata");
            }

            // Done
            return locationIds;
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

                throw new Exception("Error while getting sharepointIds");
            }
            if (sharepointIds == null) throw new FileNotFoundException("SharepointIds not found");

            // Get Drive
            Drive? drive;
            try
            {
                drive = await _driveRepository.GetDriveAsync(sharepointIds.SiteId, sharepointIds.ListId);
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                throw new Exception("Error while getting drive");
            }
            if (drive == null) throw new FileNotFoundException("Drive not found");

            // Get DriveItem
            DriveItem? driveItem;
            try
            {
                driveItem = await _driveRepository.GetDriveItemAsync(sharepointIds.SiteId, sharepointIds.ListId, sharepointIds.ListItemId);
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                throw new Exception("Error while getting driveItem");
            }
            if (driveItem == null) throw new FileNotFoundException("DriveItem not found");

            // Create new version
            try
            {
                using (var ms = new MemoryStream())
                {
                    await Request.Body.CopyToAsync(ms);
                    ms.Position = 0;

                    await _graphClient.Drives[drive.Id].Items[driveItem.Id].Request().UpdateAsync(driveItem);
                }
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                throw new Exception("Error while updating driveItem");
            }

            return true;
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
                    metadata.AdditionalMetadata[fieldMapping.FieldName] = value;
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