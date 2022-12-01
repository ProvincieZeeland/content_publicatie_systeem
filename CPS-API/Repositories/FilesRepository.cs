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

        public FilesRepository(GraphServiceClient graphClient, IContentIdRepository contentIdRepository)
        {
            _graphClient = graphClient;
            _contentIdRepository = contentIdRepository;
        }

        public Task<ContentIds> CreateAsync(CpsFile file)
        {
            // Create new file in fileStorage with filename + content in specified location (using site/web/list or drive ids)
            // Find all missing ids and return them

            throw new NotImplementedException();
        }

        public Task<CpsFile> GetAsync(string contentId)
        {
            // Find file info in documents table by contentid
            // Find file in SharePoint using ids
            // create object with sharepoint fields metadata + url to item

            throw new NotImplementedException();
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
            if (sharepointIDs.ContentIds == null) throw new Exception("Item cannot be found");

            var item = await _graphClient.Sites[sharepointIDs.ContentIds.SiteId].Lists[sharepointIDs.ContentIds.ListId].Drive.Items[sharepointIDs.ContentIds.ListItemId].CreateLink("view").Request().PostAsync();
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

        public Task<bool> UpdateMetadataAsync(CpsFile file)
        {
            // Find file info in documents table by contentid
            // Find file in SharePoint using ids
            // update sharepoint fields with metadata

            throw new NotImplementedException();
        }
    }
}
