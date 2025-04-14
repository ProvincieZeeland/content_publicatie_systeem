using System.Net;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.SharePoint.Client;
using ChangeType = Microsoft.SharePoint.Client.ChangeType;
using Constants = CPS_API.Models.Constants;
using ListItem = Microsoft.Graph.Models.ListItem;
using SharePointClientList = Microsoft.SharePoint.Client.List;

namespace CPS_API.Repositories
{
    public interface IListRepository
    {
        Task<ListItem> GetListItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false);

        Task<ListItem> AddListItemAsync(string siteId, string listId, ListItem listItem, bool getAsUser = false);

        Task UpdateListItemAsync(string siteId, string listId, string listItemId, FieldValueSet fields, bool getAsUser = false);

        Task<List<ListItem>?> GetListItemsAsync(string siteId, string listId, string fieldName, string fieldValue, bool getAsUser = false);

        Task<SharePointListItemsDelta> GetListAndFilteredChangesAsync(string siteUrl, string listId, string? changeToken);

        Task<List<ColumnDefinition>?> GetColumnsAsync(string siteId, string listId, bool getAsUser = false);
    }

    public class ListRepository : IListRepository
    {
        private readonly GlobalSettings _globalSettings;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly GraphServiceClient _graphAppServiceClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly CertificateService _certificateService;

        public ListRepository(
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            GraphServiceClient graphServiceClient,
            TelemetryClient telemetryClient,
            CertificateService certificateService,
            ITokenAcquisition tokenAcquisition)
        {
            _globalSettings = settings.Value;
            _graphServiceClient = graphServiceClient;
            _graphAppServiceClient = new GraphServiceClient(new BaseBearerTokenAuthenticationProvider(new AppOnlyAuthenticationProvider(tokenAcquisition, settings)));
            _telemetryClient = telemetryClient;
            _certificateService = certificateService;
        }

        private GraphServiceClient GetGraphServiceClient(bool getAsUser)
        {
            if (getAsUser) return _graphServiceClient;
            return _graphAppServiceClient;
        }

        #region Graph

        public async Task<ListItem> GetListItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false)
        {
            try
            {
                // Find file in SharePoint using ids
                var graphServiceClient = GetGraphServiceClient(getAsUser);
                var listItem = await graphServiceClient.Sites[siteId].Lists[listId].Items[listItemId].GetAsync(x =>
                {
                    x.QueryParameters.Expand = ["fields"];
                });
                if (listItem == null) throw new FileNotFoundException($"ListItem (siteId = {siteId}, listId = {listId}, listItemId = {listItemId}) does not exist!");
                return listItem;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                throw;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
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
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var addedListItem = await graphServiceClient.Sites[siteId].Lists[listId].Items.PostAsync(listItem);
            if (addedListItem == null) throw new CpsException($"Error while adding listItem (siteId={siteId}, listId={listId})");
            return addedListItem;
        }

        public async Task UpdateListItemAsync(string siteId, string listId, string listItemId, FieldValueSet fields, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var updatedFields = await graphServiceClient.Sites[siteId].Lists[listId].Items[listItemId].Fields.PatchAsync(fields);
            if (updatedFields == null) throw new CpsException($"Error while updating listItem (siteId={siteId}, listId={listId}, listItemId={listItemId})");
        }

        public async Task<List<ListItem>?> GetListItemsAsync(string siteId, string listId, string fieldName, string fieldValue, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var response = await graphServiceClient.Sites[siteId].Lists[listId].Items.GetAsync(x =>
            {
                x.QueryParameters.Expand = new[] { Constants.SelectFields };
                x.QueryParameters.Filter = $"Fields/{fieldName} eq '{fieldValue}'";
            });
            if (response == null) throw new CpsException($"Error while getting listItems (siteId={siteId}, listId={listId}, fieldName={fieldName}, fieldValue={fieldValue})");

            // list which contains all items
            var listItems = new List<ListItem>();
            var pageIterator = PageIterator<ListItem, ListItemCollectionResponse>.CreatePageIterator(graphServiceClient, response, item =>
            {
                listItems.Add(item);
                return true;
            });
            await pageIterator.IterateAsync();
            return listItems;
        }

        public async Task<List<ColumnDefinition>?> GetColumnsAsync(string siteId, string listId, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var response = await graphServiceClient.Sites[siteId].Lists[listId].Columns.GetAsync(x =>
            {
                x.QueryParameters.Select = ["hidden", "DisplayName", "Name"];
            });
            if (response == null) throw new CpsException($"Error while getting columns (siteId={siteId}, listId={listId})");

            // list which contains all items
            var columnDefinitions = new List<ColumnDefinition>();
            var pageIterator = PageIterator<ColumnDefinition, ColumnDefinitionCollectionResponse>.CreatePageIterator(graphServiceClient, response, item =>
            {
                columnDefinitions.Add(item);
                return true;
            });
            await pageIterator.IterateAsync();
            return columnDefinitions;
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

        public async Task<SharePointListItemsDelta> GetListAndFilteredChangesAsync(string siteUrl, string listId, string? changeToken)
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
                if (ex is ServerException serverEx && !IsValidChangeToken(serverEx))
                {
                    throw new CpsException("Invalid ChangeToken");
                }

                _telemetryClient.TrackException(new CpsException($"Error while getting list changes {siteUrl}", ex));
                throw new CpsException($"Error while getting list changes {siteUrl}");
            }

            // Get correct and unique changes
            return FilterChangesOnDeletedAndUnique(changes);
        }

        private static bool IsValidChangeToken(ServerException serverEx)
        {
            // The Exception that is thrown when ChangeTokenStart is invalid:
            //'Microsoft.SharePoint.Client.ServerException' with the following typeNames and corresponding errorCodes
            if ((serverEx.ServerErrorTypeName == Constants.InvalidChangeTokenServerErrorTypeName && serverEx.ServerErrorCode == Constants.InvalidChangeTokenErrorCode)
            || (serverEx.ServerErrorTypeName == Constants.FormatChangeTokenServerErrorTypeName && serverEx.ServerErrorCode == Constants.FormatChangeTokenErrorCode)
            || (serverEx.ServerErrorTypeName == Constants.InvalidOperationChangeTokenServerErrorTypeName && serverEx.ServerErrorCode == Constants.InvalidOperationChangeTokenErrorCode)
            || ((serverEx.Message.Equals(Constants.InvalidChangeTokenTimeErrorMessageDutch) || serverEx.Message.Equals(Constants.InvalidChangeTokenTimeErrorMessageEnglish)) && serverEx.ServerErrorCode == Constants.InvalidChangeTokenTimeErrorCode)
            || ((serverEx.Message.Equals(Constants.InvalidChangeTokenWrondObjectErrorMessageDutch) || serverEx.Message.Equals(Constants.InvalidChangeTokenWrongObjectErrorMessageEnglish)) && serverEx.ServerErrorCode == Constants.InvalidChangeTokenWrongObjectErrorCode))
            {
                return false;
            }
            return true;
        }

        private static async Task<SharePointListItemsDelta> GetDeltaListItemsAndLastChangeToken(ClientContext context, SharePointClientList list, string? changeToken)
        {
            ChangeCollection changeCollection;
            var changes = new SharePointListItemsDelta
            {
                Items = []
            };
            if (changeToken != null) changes.NewChangeToken = changeToken;
            do
            {
                changeCollection = await GetListChangesAsync(context, list, changes.NewChangeToken);
                changes.Items.AddRange(getDeltaListItems(changeCollection));
                changes.NewChangeToken = changeCollection.LastChangeToken.StringValue;
            }
            while (changeCollection.HasMoreChanges);
            return changes;
        }

        private static async Task<ChangeCollection> GetListChangesAsync(ClientContext context, SharePointClientList list, string lastChangeToken)
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

        private static List<SharePointListItemDelta> getDeltaListItems(ChangeCollection changes)
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