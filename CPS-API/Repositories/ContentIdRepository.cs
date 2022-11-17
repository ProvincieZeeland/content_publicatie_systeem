using CPS_API.Models;

namespace CPS_API.Repositories
{
    public interface IContentIdRepository
    {
        Task<string> GenerateContentIdAsync(ContentIds sharePointIds);
    }

    public class ContentIdRepository : IContentIdRepository
    {
        public Task<string> GenerateContentIdAsync(ContentIds sharePointIds)
        {
            throw new NotImplementedException();
        }
    }
}
