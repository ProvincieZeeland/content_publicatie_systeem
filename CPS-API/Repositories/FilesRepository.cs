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
            throw new NotImplementedException();
        }

        public Task<CpsFile> GetAsync(string contentId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> UpdateContentAsync(CpsFile file)
        {
            throw new NotImplementedException();
        }

        public Task<bool> UpdateMetadataAsync(CpsFile file)
        {
            throw new NotImplementedException();
        }
    }
}
