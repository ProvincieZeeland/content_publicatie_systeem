using CPS_API.Models;

namespace CPS_API.Repositories
{
    public interface IFilesRepository
    {
        Task<CpsFile> GetAsync(string contentId);

        Task<bool> CreateAsync(CpsFile file);

        Task<bool> UpdateContentAsync(CpsFile file);

        Task<bool> UpdateMetadataAsync(CpsFile file);
    }

    public class FilesRepository : IFilesRepository
    {
        public Task<bool> CreateAsync(CpsFile file)
        {
            // Create new file in fileStorage with filename + content in specified location (using site/web/list or drive ids)
            throw new NotImplementedException();
        }

        public Task<CpsFile> GetAsync(string contentId)
        {
            // Find file info in documents table by contentid
            // Find file in SharePoint using ids
            // create object with sharepoint fields metadata + url to item

            throw new NotImplementedException();
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
