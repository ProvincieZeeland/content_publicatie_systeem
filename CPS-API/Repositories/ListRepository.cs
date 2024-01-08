using Microsoft.Graph;
using Microsoft.Identity.Web;

namespace CPS_API.Repositories
{
    public interface IListRepository
    {
        Task<Site> GetSiteByUrlAsync(string serverRelativeUrl, bool getAsUser = false);

        Task<Site> GetSiteAsync(string siteId, bool getAsUser = false);

        Task<ListItem?> GetListItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false);

        Task<ListItem> AddListItemAsync(string siteId, string listId, ListItem listItem, bool getAsUser = false);

        Task UpdateListItemAsync(string siteId, string listId, string listItemId, FieldValueSet fields, bool getAsUser = false);

        Task<List<ListItem>?> GetListItemsAsync(string siteId, string listId, string fieldName, string fieldValue, bool getAsUser = false);
    }

    public class ListRepository : IListRepository
    {
        private readonly GraphServiceClient _graphClient;

        public ListRepository(GraphServiceClient graphClient)
        {
            _graphClient = graphClient;
        }

        public async Task<Site> GetSiteByUrlAsync(string serverRelativeUrl, bool getAsUser = false)
        {
            var request = _graphClient.Sites[serverRelativeUrl].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<Site> GetSiteAsync(string siteId, bool getAsUser = false)
        {
            var request = _graphClient.Sites[siteId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<ListItem?> GetListItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false)
        {
            // Find file in SharePoint using ids
            var queryOptions = new List<QueryOption>()
            {
                new QueryOption("expand", "fields")
            };

            var request = _graphClient.Sites[siteId].Lists[listId].Items[listItemId].Request(queryOptions);
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<ListItem> AddListItemAsync(string siteId, string listId, ListItem listItem, bool getAsUser = false)
        {
            var request = _graphClient.Sites[siteId].Lists[listId].Items.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.AddAsync(listItem);
        }

        public async Task UpdateListItemAsync(string siteId, string listId, string listItemId, FieldValueSet fields, bool getAsUser = false)
        {
            var request = _graphClient.Sites[siteId].Lists[listId].Items[listItemId].Fields.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            await request.UpdateAsync(fields);
        }

        public async Task<List<ListItem>?> GetListItemsAsync(string siteId, string listId, string fieldName, string fieldValue, bool getAsUser = false)
        {
            var request = _graphClient.Sites[siteId].Lists[listId].Items.Request().Expand("Fields").Filter($"Fields/{fieldName} eq '{fieldValue}'");
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            var listItemsPage = await request.GetAsync();
            return listItemsPage?.CurrentPage?.ToList();
        }
    }
}