using System.Net;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.SharePoint.Client;
using ChangeType = Microsoft.SharePoint.Client.ChangeType;
using Constants = CPS_API.Models.Constants;
using ListItem = Microsoft.Graph.ListItem;
using SharePointClientList = Microsoft.SharePoint.Client.List;

namespace CPS_API.Repositories
{
    public interface IListRepository
    {
        Task<ListItem> GetListItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false);

        Task<ListItem> AddListItemAsync(string siteId, string listId, ListItem listItem, bool getAsUser = false);

        Task UpdateListItemAsync(string siteId, string listId, string listItemId, FieldValueSet fields, bool getAsUser = false);

        Task<List<ListItem>?> GetListItemsAsync(string siteId, string listId, string fieldName, string fieldValue, bool getAsUser = false);

        Task<SharePointListItemsDelta> GetListAndFilteredChangesAsync(string siteUrl, string listId, string changeToken);
    }

    public class ListRepository : IListRepository
    {
        private readonly GlobalSettings _globalSettings;
        private readonly GraphServiceClient _graphClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly CertificateService _certificateService;

        public ListRepository(
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            GraphServiceClient graphClient,
            TelemetryClient telemetryClient,
            CertificateService certificateService)
        {
            _globalSettings = settings.Value;
            _graphClient = graphClient;
            _telemetryClient = telemetryClient;
            _certificateService = certificateService;
        }

        #region Graph

        public async Task<ListItem> GetListItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false)
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

            try
            {
                var listItem = await request.GetAsync();
                if (listItem == null) throw new FileNotFoundException($"ListItem (siteId = {siteId}, listId = {listId}, listItemId = {listItemId}) does not exist!");
                return listItem;
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw;
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"ListItem (siteId = {siteId}, listId = {listId}, listItemId = {listItemId}) does not exist!");
            }
            catch (Exception ex) when (ex is MsalUiRequiredException || ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CpsException($"Error while getting listItem (siteId = {siteId}, listId = {listId}, listItemId = {listItemId})", ex);
            }
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

        #endregion

        #region SharePoint API

        private static async Task<SharePointClientList> GetAndLoadListAsync(ClientContext context, string listId)
        {
            var list = context.Web.Lists.GetById(new Guid(listId));
            context.Load(list);
            await context.ExecuteQueryRetryAsync(1);
            return list;
        }

        public async Task<SharePointListItemsDelta> GetListAndFilteredChangesAsync(string siteUrl, string listId, string changeToken)
        {
            var certificate = await _certificateService.GetCertificateAsync();
            using var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, certificate, _globalSettings.TenantId);
            using ClientContext context = await authenticationManager.GetContextAsync(siteUrl);

            // Get list
            var list = await GetAndLoadListAsync(context, listId);

            SharePointListItemsDelta changes;
            try
            {
                changes = await GetDeltaListItemsAndLastChangeToken(context, list, changeToken);
            }
            catch (Exception ex)
            {
                if (ex is ServerException)
                {
                    // The Exception that is thrown when ChangeTokenStart is invalid:
                    //'Microsoft.SharePoint.Client.ServerException' with the following typeNames and corresponding errorCodes
                    var serverEx = ex as ServerException;
                    if ((serverEx.ServerErrorTypeName == Constants.InvalidChangeTokenServerErrorTypeName && serverEx.ServerErrorCode == Constants.InvalidChangeTokenErrorCode)
                    || (serverEx.ServerErrorTypeName == Constants.FormatChangeTokenServerErrorTypeName && serverEx.ServerErrorCode == Constants.FormatChangeTokenErrorCode)
                    || (serverEx.ServerErrorTypeName == Constants.InvalidOperationChangeTokenServerErrorTypeName && serverEx.ServerErrorCode == Constants.InvalidOperationChangeTokenErrorCode)
                    || ((serverEx.Message.Equals(Constants.InvalidChangeTokenTimeErrorMessageDutch) || serverEx.Message.Equals(Constants.InvalidChangeTokenTimeErrorMessageEnglish)) && serverEx.ServerErrorCode == Constants.InvalidChangeTokenTimeErrorCode)
                    || ((serverEx.Message.Equals(Constants.InvalidChangeTokenWrondObjectErrorMessageDutch) || serverEx.Message.Equals(Constants.InvalidChangeTokenWrongObjectErrorMessageEnglish)) && serverEx.ServerErrorCode == Constants.InvalidChangeTokenWrongObjectErrorCode))
                    {
                        throw new CpsException("Invalid ChangeToken");
                    }
                }

                _telemetryClient.TrackException(new CpsException($"Error while getting list changes {siteUrl}", ex));
                throw new CpsException($"Error while getting list changes {siteUrl}");
            }

            // Get correct and unique changes
            return FilterChangesOnDeletedAndUnique(changes);
        }

        private async Task<SharePointListItemsDelta> GetDeltaListItemsAndLastChangeToken(ClientContext context, SharePointClientList list, string changeToken)
        {
            ChangeCollection changeCollection;
            var changes = new SharePointListItemsDelta
            {
                Items = new List<SharePointListItemDelta>(),
                NewChangeToken = changeToken
            };
            do
            {
                changeCollection = await GetListChangesAsync(context, list, changes.NewChangeToken);
                changes.Items.AddRange(getDeltaListItems(changeCollection));
                changes.NewChangeToken = changeCollection.LastChangeToken.StringValue;
            }
            while (changeCollection.HasMoreChanges);
            return changes;
        }

        private async Task<ChangeCollection> GetListChangesAsync(ClientContext context, SharePointClientList list, string lastChangeToken)
        {
            ChangeQuery query = new ChangeQuery(false, false)
            {
                Add = true,
                Item = true,
                Update = true,
                Move = true,
                DeleteObject = true,
                SystemUpdate = true
            };
            if (!string.IsNullOrEmpty(lastChangeToken))
            {
                query.ChangeTokenStart = new ChangeToken
                {
                    StringValue = lastChangeToken
                };
            }

            var changeCollection = list.GetChanges(query);
            context.Load(
                changeCollection,
                cc => cc.LastChangeToken,
                cc => cc.HasMoreChanges,
                cc => cc.Include(
                    c => ((ChangeItem)c).ItemId,
                    c => ((ChangeItem)c).UniqueId,
                    c => ((ChangeItem)c).ContentTypeId,
                    c => ((ChangeItem)c).ChangeType,
                    c => ((ChangeItem)c).ActivityType,
                    c => ((ChangeItem)c).Editor,
                    c => ((ChangeItem)c).EditorLoginName,
                    c => ((ChangeItem)c).IsRecycleBinOperation
                    ));

            await context.ExecuteQueryRetryAsync(1);
            return changeCollection;
        }

        private List<SharePointListItemDelta> getDeltaListItems(ChangeCollection changes)
        {
            var deltaListItems = new List<SharePointListItemDelta>();
            foreach (var change in changes)
            {
                if (change is ChangeItem changeItem)
                {
                    var deltaListItem = new SharePointListItemDelta(changeItem.ItemId, changeItem.ChangeType);
                    deltaListItems.Add(deltaListItem);
                }
            }
            return deltaListItems;
        }

        private static SharePointListItemsDelta FilterChangesOnDeletedAndUnique(SharePointListItemsDelta changes)
        {
            var deletedItemIds = changes.Items.Where(d => d.ChangeType == ChangeType.DeleteObject).DistinctBy(d => d.ListItemId).Select(d => d.ListItemId).ToList();
            var uniqueItems = changes.Items.DistinctBy(d => d.ListItemId).ToList();
            changes.Items = uniqueItems.Where(d => !deletedItemIds.Contains(d.ListItemId)).ToList();
            return changes;
        }

        #endregion
    }
}