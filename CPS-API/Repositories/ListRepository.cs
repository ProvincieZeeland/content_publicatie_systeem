using System.Net;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.SharePoint.Client;
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

        Task<SharePointListItemsDelta> GetListAndChangesAsync(string siteUrl, string listId, string? changeToken);

        Task<List<ColumnDefinition>?> GetColumnsAsync(string siteId, string listId, bool getAsUser = false);
    }

    public class ListRepository : IListRepository
    {
        private readonly GlobalSettings _globalSettings;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly GraphServiceClient _graphAppServiceClient;
        private readonly ILogger _logger;
        private readonly CertificateService _certificateService;

        public ListRepository(
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            GraphServiceClient graphServiceClient,
            ILogger<ListRepository> logger,
            CertificateService certificateService,
            ITokenAcquisition tokenAcquisition)
        {
            _globalSettings = settings.Value;
            _graphServiceClient = graphServiceClient;
            _graphAppServiceClient = new GraphServiceClient(new BaseBearerTokenAuthenticationProvider(new AppOnlyAuthenticationProvider(tokenAcquisition, settings)));
            _logger = logger;
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
                x.QueryParameters.Expand = new[] { Constants.Selectors.Fields };
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

        public async Task<SharePointListItemsDelta> GetListAndChangesAsync(string siteUrl, string listId, string? changeToken)
        {
            var certificate = await _certificateService.GetCertificateAsync();
            using var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, certificate, _globalSettings.TenantId);
            using ClientContext context = await authenticationManager.GetContextAsync(siteUrl);

            // Get list
            var list = await GetAndLoadListAsync(context, listId);

            try
            {
                return await GetDeltaListItemsAndLastChangeToken(list, changeToken);
            }
            catch (Exception ex)
            {
                if (ex is ServerException serverEx && !IsValidChangeToken(serverEx))
                {
                    throw new CpsException("Invalid ChangeToken");
                }

                var errorMessage = $"Error while getting list changes {siteUrl}";
                _logger.LogError(ex, "{ErrorMessage}", errorMessage);
                throw new CpsException(errorMessage);
            }
        }

        private static bool IsValidChangeToken(ServerException serverEx)
        {
            // The Exception that is thrown when ChangeTokenStart is invalid:
            //'Microsoft.SharePoint.Client.ServerException' with the following typeNames and corresponding errorCodes
            if ((serverEx.ServerErrorTypeName == Constants.ChangeTokenErrors.InvalidServerErrorTypeName && serverEx.ServerErrorCode == Constants.ChangeTokenErrors.InvalidErrorCode)
            || (serverEx.ServerErrorTypeName == Constants.ChangeTokenErrors.FormatServerErrorTypeName && serverEx.ServerErrorCode == Constants.ChangeTokenErrors.FormatErrorCode)
            || (serverEx.ServerErrorTypeName == Constants.ChangeTokenErrors.InvalidOperationServerErrorTypeName && serverEx.ServerErrorCode == Constants.ChangeTokenErrors.InvalidOperationCode)
            || ((serverEx.Message.Equals(Constants.ChangeTokenErrors.InvalidTimeErrorMessageDutch) || serverEx.Message.Equals(Constants.ChangeTokenErrors.InvalidTimeErrorMessageEnglish)) && serverEx.ServerErrorCode == Constants.ChangeTokenErrors.InvalidTimeErrorCode)
            || ((serverEx.Message.Equals(Constants.ChangeTokenErrors.InvalidWrondObjectErrorMessageDutch) || serverEx.Message.Equals(Constants.ChangeTokenErrors.InvalidWrongObjectErrorMessageEnglish)) && serverEx.ServerErrorCode == Constants.ChangeTokenErrors.InvalidWrongObjectErrorCode))
            {
                return false;
            }
            return true;
        }

        private static async Task<SharePointListItemsDelta> GetDeltaListItemsAndLastChangeToken(SharePointClientList list, string? changeToken)
        {
            ChangeCollection changeCollection;
            var changes = new SharePointListItemsDelta
            {
                Items = []
            };
            if (changeToken != null) changes.NewChangeToken = changeToken;
            do
            {
                changeCollection = await GetListChangesAsync(list, changes.NewChangeToken);
                changes.Items.AddRange(getDeltaListItems(changeCollection));

                if (changeCollection.HasMoreChanges)
                {
                    changes.NewChangeToken = changeCollection.LastChangeToken.StringValue;
                }
                else if (changeCollection.Count > 0)
                {
                    // Baseline token for the next scheduled run
                    changes.NewChangeToken = changeCollection[changeCollection.Count - 1].ChangeToken.StringValue;
                }
            }
            while (changeCollection.HasMoreChanges);
            return changes;
        }

        private static async Task<ChangeCollection> GetListChangesAsync(SharePointClientList list, string lastChangeToken, int pageSize = 200)
        {
            ChangeQuery query = new ChangeQuery(false, false)
            {
                Add = true,
                Item = true,
                Update = true,
                Move = true,
                DeleteObject = true,
                SystemUpdate = true,
                Rename = true,
                FetchLimit = pageSize
            };
            if (!string.IsNullOrEmpty(lastChangeToken))
            {
                query.ChangeTokenStart = new ChangeToken
                {
                    StringValue = lastChangeToken
                };
            }

            var changeCollection = list.GetChanges(query);
            list.Context.Load(
                changeCollection,
                cc => cc.HasMoreChanges,
                cc => cc.LastChangeToken,
                cc => cc.Include(
                    c => ((ChangeItem)c).ItemId,
                    c => ((ChangeItem)c).ChangeType,
                    c => ((ChangeItem)c).ChangeToken
                    ));

            await list.Context.ExecuteQueryRetryAsync(1);
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

        #endregion
    }
}