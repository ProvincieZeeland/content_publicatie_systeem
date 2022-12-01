using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.Graph;

namespace CPS_API.Repositories
{
    public interface IFilesRepository
    {
        Task<CpsFile> GetAsync(string contentId);

        Task<string> GetUrlAsync(string contentId);

        Task<ContentIds> CreateAsync(CpsFile file);

        Task<bool> UpdateContentAsync(CpsFile file);

        Task<bool> UpdateMetadataAsync(CpsFile file);
    }

    public class FilesRepository : IFilesRepository
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IContentIdRepository _contentIdRepository;
        private readonly GlobalSettings _globalSettings;

        public FilesRepository(GraphServiceClient graphClient, IContentIdRepository contentIdRepository, Microsoft.Extensions.Options.IOptions<GlobalSettings> settings)
        {
            _graphClient = graphClient;
            _contentIdRepository = contentIdRepository;
            _globalSettings = settings.Value;
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
            var documentIds = await _contentIdRepository.GetSharePointIdsAsync(contentId);
            if (documentIds == null) throw new FileNotFoundException("ContentId not found");

            // Find file in SharePoint using ids
            var queryOptions = new List<QueryOption>()
            {
                new QueryOption("expand", "fields")
            };

            var file = await _graphClient.Sites[documentIds.ContentIds.SiteId].Lists[documentIds.ContentIds.ListId].Items[documentIds.ContentIds.ListItemId].Request(queryOptions).GetAsync();
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

        public async Task<string> GetUrlAsync(string contentId)
        {
            // Sharepoint id's bepalen.
            var sharepointIDs = await _contentIdRepository.GetSharePointIdsAsync(contentId);
            if (sharepointIDs == null)
            {
                throw new FileNotFoundException();
            }

            // Consider different error messages
            if (sharepointIDs.ContentIds == null) throw new FileNotFoundException("Item cannot be found");

            var item = await _graphClient.Drives[sharepointIDs.ContentIds.DriveId].Items[sharepointIDs.ContentIds.DriveItemId].CreateLink("view").Request().PostAsync();
            if (item == null)
            {
                return null;
            }
            return item.Link.WebUrl;
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
