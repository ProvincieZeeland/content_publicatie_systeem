using CPS_API.Models;

namespace CPS_API.Repositories
{
    public interface IContentIdRepository
    {
        Task<string> GetContentIdAsync(ContentIds sharePointIds);

        Task<string> GenerateContentIdAsync(ContentIds sharePointIds);
    }

    public class ContentIdRepository : IContentIdRepository
    {
        public Task<string> GenerateContentIdAsync(ContentIds sharePointIds)
        {
            // Check if sharepointIds already in table, if so; return contentId.

            // Get sequencenr for contentid from table
            // Increase sequencenr and store in table
            // Create new contentId
            // find driveID + driveItemID for object
            // Store contentId + backend ids in table


            throw new NotImplementedException();
        }

        public Task<string> GetContentIdAsync(ContentIds sharePointIds)
        {
            // find contentId in table by sharepoint siteId + webId + listId + itemId

            throw new NotImplementedException();
        }
    }
}
