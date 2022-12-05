using CPS_API.Models;
using Microsoft.Graph;
using System.Text.Json;

namespace CPS_API.Repositories
{
    public interface IFilesRepository
    {
        Task<CpsFile> GetAsync(string contentId);

        Task<string> GetUrlAsync(string contentId);

        Task<ContentIds> CreateAsync(CpsFile file);

        Task<bool> UpdateContentAsync(CpsFile file);

        Task<bool> UpdateMetadataAsync(CpsFile file);

        Task<string> GetFileMetadataAsync(string contentId);
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

        public Task<ContentIds> CreateAsync(CpsFile file)
        {
            // Create new file in fileStorage with filename + content in specified location (using site/web/list or drive ids)
            // Find all missing ids and return them

            throw new NotImplementedException();
        }

        private async Task<ListItem?> getListItem(string contentId)
        {
            // Find file info in documents table by contentid
            var sharepointIds = await _contentIdRepository.GetSharepointIdsAsync(contentId);
            if (sharepointIds == null) throw new FileNotFoundException($"SharepointIds (conentId = {contentId}) does not exist!");

            // Find file in SharePoint using ids
            var queryOptions = new List<QueryOption>()
            {
                new QueryOption("expand", "fields")
            };

            var file = await _graphClient.Sites[sharepointIds.SiteId].Lists[sharepointIds.ListId].Items[sharepointIds.ListItemId].Request(queryOptions).GetAsync();
            return file;
        }

        public async Task<CpsFile> GetAsync(string contentId)
        {
            ListItem? file = await getListItem(contentId);
            if (file == null) throw new FileNotFoundException();

            FileInformation metadata = new FileInformation();
            metadata.FileName = file.Name;
            metadata.AdditionalMetadata = new FileMetadata();

            if (!string.IsNullOrEmpty(file.Name) && file.Name.Contains('.'))
                metadata.FileExtension = file.Name.Split('.')[1];

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
                try
                {
                    var value = file.Fields.AdditionalData[fieldMapping.SpoColumnName];
                    if (fieldMapping.FieldName.Equals(nameof(FileInformation.ExternalApplication), StringComparison.InvariantCultureIgnoreCase))
                    {
                        metadata.ExternalApplication = (string)value;
                    }
                    else
                    {
                        metadata.AdditionalMetadata[fieldMapping.FieldName] = value;
                    }

                }
                catch
                {
                    metadata.AdditionalMetadata[fieldMapping.FieldName] = null;
                    // log warning to insights?
                }
            }


            return new CpsFile
            {
                Metadata = metadata
            };
        }

        public async Task<string?> GetUrlAsync(string contentId)
        {
            // Get Listitem
            var driveItem = await GetDriveItemAsync(contentId);
            if (driveItem == null) throw new FileNotFoundException($"DriveItem (contentId = {contentId}) does not exist!");

            // Get url
            return driveItem.WebUrl;
        }

        private async Task<DriveItem> GetDriveItemAsync(string contentId)
        {
            ContentIds? sharepointIds;
            try
            {
                sharepointIds = await _contentIdRepository.GetSharepointIdsAsync(contentId);
            }
            catch (Exception ex) when (ex.InnerException is not UnauthorizedAccessException && ex is not FileNotFoundException)
            {
                throw new Exception("Error while getting sharePointIds");
            }
            if (sharepointIds == null) throw new FileNotFoundException($"SharepointIds (contentId = {contentId}) does not exist!");

            try
            {
                return await _driveRepository.GetDriveItemAsync(sharepointIds.SiteId, sharepointIds.ListId, sharepointIds.ListItemId);
            }
            catch (Exception ex) when (ex.InnerException is not UnauthorizedAccessException)
            {
                throw new Exception("Error while getting driveItem");
            }
        }

        public async Task<string> GetFileMetadataAsync(string contentId)
        {
            // Get Listitem
            ListItem? file;
            try
            {
                file = await getListItem(contentId);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException();
            }
            catch (Exception ex)
            {
                throw new Exception("Error while getting listItem");
            }
            if (file == null) throw new FileNotFoundException();

            // Map metadata
            var metadata = file.Fields as MetadataFieldValueSet;
            if (metadata == null) throw new FileNotFoundException();
            return JsonSerializer.Serialize<MetadataFieldValueSet>(metadata);
        }

        public Task<bool> UpdateContentAsync(CpsFile file)
        {
            // Find file info in documents table by contentid
            // Find file in SharePoint using ids
            // create new version of document in sharepoint and upload content

            throw new NotImplementedException();
        }

        public async Task<bool> UpdateMetadataAsync(CpsFile file)
        {
            if (file.Metadata == null) throw new ArgumentNullException("file.Metadata");
            if (file.Metadata.AdditionalMetadata == null) throw new ArgumentNullException("file.Metadata.AdditionalMetadata");
            if (file.Metadata.Ids == null) throw new ArgumentNullException("file.Metadata.Ids");

            ListItem? item = await getListItem(file.Metadata.Ids.ContentId);
            if (item == null) throw new FileNotFoundException();

            // map received metadata to SPO object
            foreach (var fieldMapping in _globalSettings.MetadataSettings)
            {
                try
                {
                    object? value;
                    if (fieldMapping.FieldName.Equals(nameof(FileInformation.ExternalApplication), StringComparison.InvariantCultureIgnoreCase))
                    {
                        value = file.Metadata.ExternalApplication;
                    }
                    else
                    {
                        value = file.Metadata.AdditionalMetadata[fieldMapping.FieldName];
                    }

                    item.Fields.AdditionalData[fieldMapping.SpoColumnName] = value;
                }
                catch
                {
                    throw new ArgumentException("Cannot parse received input to valid SharePoint field data", fieldMapping.FieldName);
                }
            }

            // update sharepoint fields with metadata
            var updatedItem = await _graphClient.Sites[file.Metadata.Ids.SiteId].Lists[file.Metadata.Ids.ListId].Items[file.Metadata.Ids.ListItemId].Request().PutAsync(item);
            if (updatedItem != null) return true;

            return false;
        }
    }
}
