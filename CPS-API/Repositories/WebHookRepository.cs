using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Options;
using Microsoft.SharePoint.Client;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Constants = CPS_API.Models.Constants;
using GraphListItem = Microsoft.Graph.ListItem;
using GraphSite = Microsoft.Graph.Site;

namespace CPS_API.Repositories
{
    public interface IWebHookRepository
    {
        Task<SubscriptionModel> CreateWebHookAsync(GraphSite site, string listId);

        Task<string?> HandleSharePointNotificationAsync(string? validationToken, ResponseModel<WebHookNotification>? notificationsResponse);

        Task<string> HandleDropOffNotificationAsync(WebHookNotification notification);
    }

    public class WebHookRepository : IWebHookRepository
    {
        private readonly IDriveRepository _driveRepository;
        private readonly IListRepository _listRepository;
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly IMetadataRepository _metadataRepository;
        private readonly IFilesRepository _filesRepository;
        private readonly ISettingsRepository _settingsRepository;
        private readonly ISharePointRepository _sharePointRepository;

        private readonly StorageTableService _storageTableService;
        private readonly EmailService _emailService;

        private readonly GlobalSettings _globalSettings;

        private readonly TelemetryClient _telemetryClient;

        public WebHookRepository(
            IDriveRepository driveRepository,
            IListRepository listRepository,
            IObjectIdRepository objectIdRepository,
            IMetadataRepository metadataRepository,
            IFilesRepository filesRepository,
            ISettingsRepository settingsRepository,
            ISharePointRepository sharePointRepository,
            StorageTableService storageTableService,
            EmailService emailService,
            IOptions<GlobalSettings> settings,
            TelemetryClient telemetryClient)
        {
            _driveRepository = driveRepository;
            _listRepository = listRepository;
            _objectIdRepository = objectIdRepository;
            _metadataRepository = metadataRepository;
            _filesRepository = filesRepository;
            _settingsRepository = settingsRepository;
            _sharePointRepository = sharePointRepository;
            _storageTableService = storageTableService;
            _emailService = emailService;
            _globalSettings = settings.Value;
            _telemetryClient = telemetryClient;
        }

        #region Create Webhook

        public async Task<SubscriptionModel> CreateWebHookAsync(GraphSite site, string listId)
        {
            using (var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, StoreName.My, StoreLocation.CurrentUser, _globalSettings.CertificateThumbprint, _globalSettings.TenantId))
            {
                var accessToken = await authenticationManager.GetAccessTokenAsync(site.WebUrl);
                if (accessToken == null)
                {
                    throw new CpsException("Error while getting accessToken");
                }

                var subscription = await AddListWebHookAsync(site.WebUrl, listId, _globalSettings.WebHookEndPoint, accessToken, _globalSettings.WebHookClientState);
                if (subscription == null)
                {
                    throw new CpsException("Error while adding webhook");
                }

                // Save expiration date for renewing webhook.
                // Save subscriptionId for deleting webhook.
                await _settingsRepository.SaveSettingAsync(Constants.DropOffSubscriptionId, subscription.Id);
                await _settingsRepository.SaveSettingAsync(Constants.DropOffSubscriptionExpirationDateTime, subscription.ExpirationDateTime);

                return subscription;
            }
        }

        /// <summary>
        /// This method adds a web hook to a SharePoint list.
        /// Note that you need your webhook endpoint being passed into this method to be up and running and reachable from the internet
        /// </summary>
        /// <param name="siteUrl">Url of the site holding the list</param>
        /// <param name="listId">Id of the list</param>
        /// <param name="webHookEndPoint">Url of the web hook service endpoint (the one that will be called during an event)</param>
        /// <param name="accessToken">Access token to authenticate against SharePoint</param>
        /// <param name="validityInMonths">Optional web hook validity in months, defaults to 3 months, max is 6 months</param>
        /// <returns>subscription ID of the new web hook</returns>
        private static async Task<SubscriptionModel> AddListWebHookAsync(string siteUrl, string listId, string webHookEndPoint, string accessToken, string webHookClientState, int validityInMonths = 3)
        {
            string? responseString = null;
            using (var httpClient = new HttpClient())
            {
                string requestUrl = String.Format("{0}/_api/web/lists('{1}')/subscriptions", siteUrl, listId);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                request.Content = new StringContent(JsonConvert.SerializeObject(
                    new SubscriptionModel()
                    {
                        Resource = String.Format("{0}/_api/web/lists('{1}')", siteUrl, listId.ToString()),
                        NotificationUrl = webHookEndPoint,
                        ExpirationDateTime = DateTime.Now.AddMonths(validityInMonths).ToUniversalTime(),
                        ClientState = webHookClientState
                    }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    responseString = await response.Content.ReadAsStringAsync();
                }
                else
                {
                    // Something went wrong...
                    throw new CpsException(await response.Content.ReadAsStringAsync());
                }
            }

            return await Task.Run(() => JsonConvert.DeserializeObject<SubscriptionModel>(responseString));
        }

        #endregion Create Webhook

        #region Handle Notification

        public async Task<string?> HandleSharePointNotificationAsync(string? validationToken, ResponseModel<WebHookNotification>? notificationsResponse)
        {
            _telemetryClient.TrackTrace($"Webhook endpoint triggered!");

            // If a validation token is present, we need to respond within 5 seconds by
            // returning the given validation token. This only happens when a new
            // web hook is being added
            if (validationToken != null)
            {
                _telemetryClient.TrackTrace($"Validation token {validationToken} received");
                return validationToken;
            }

            _telemetryClient.TrackTrace($"SharePoint triggered our webhook");

            if (notificationsResponse == null)
            {
                _telemetryClient.TrackTrace($"NotificationsResponse is null");
                return null;
            }

            var notifications = notificationsResponse.Value;
            _telemetryClient.TrackTrace($"Found {notifications.Count} notifications");

            if (notifications.Count > 0)
            {
                _telemetryClient.TrackTrace($"Processing notifications...");
                foreach (var notification in notifications)
                {
                    await AddNotificationToQueueAsync(notification);
                }
            }

            // if we get here we assume the request was well received
            return null;
        }

        private async Task AddNotificationToQueueAsync(WebHookNotification notification)
        {
            var queue = await _storageTableService.GetQueue("sharepointlistwebhooknotifications");
            var message = JsonConvert.SerializeObject(notification);
            _telemetryClient.TrackTrace($"Before adding a message to the queue. Message content: {message}");
            await queue.AddMessageAsync(new CloudQueueMessage(message));
            _telemetryClient.TrackTrace($"Message added");
        }

        public async Task<string> HandleDropOffNotificationAsync(WebHookNotification notification)
        {
            // Get site
            var site = await _listRepository.GetSiteByUrlAsync(_globalSettings.HostName + ":" + notification.SiteUrl + ":/");

            // Get Changes
            var changes = await GetChangeTokenAndListChangesAsync(site.WebUrl, notification.Resource);
            if (changes.ChangeTokenInvalid)
            {
                throw new CpsException("Invalid ChangeToken");
            }

            // Process changes
            var processedItems = await ProccessListItemsAsync(site.Id, notification.Resource, changes.Items);

            // Save lastChangeToken for next notification
            await _settingsRepository.SaveSettingAsync(Constants.DropOffLastChangeToken, changes.NewChangeToken);

            // Return summary
            return GetNotificationProcessSummary(processedItems);
        }

        #region Get changes

        private async Task<SharePointListItemsDelta> GetChangeTokenAndListChangesAsync(string siteUrl, string listId)
        {
            var changeToken = await _settingsRepository.GetSetting<string>(Constants.DropOffLastChangeToken);
            if (string.IsNullOrWhiteSpace(changeToken))
            {
                _telemetryClient.TrackTrace("Change token is empty, attempting to retrieve whole change history.");
            }

            try
            {
                return await _sharePointRepository.GetListAndFilteredChangesAsync(siteUrl, listId, changeToken);
            }
            catch (Exception ex)
            {
                if (ex is ServerException)
                {
                    // The Exception that is thrown when ChangeTokenStart is invalid:
                    //'Microsoft.SharePoint.Client.ServerException' with the following typeNames and corresponding errorCodes
                    var serverEx = ex as ServerException;
                    if ((serverEx.ServerErrorTypeName == "System.ArgumentOutofRangeException" && serverEx.ServerErrorCode == Constants.ERROR_CODE_INVALID_CHANGE_TOKEN)
                    || (serverEx.ServerErrorTypeName == "System.FormatException" && serverEx.ServerErrorCode == Constants.ERROR_CODE_FORMAT_CHANGE_TOKEN)
                    || (serverEx.ServerErrorTypeName == "System.InvalidOperationException" && serverEx.ServerErrorCode == Constants.ERROR_CODE_INVALID_OPERATION_CHANGE_TOKEN)
                    || ((serverEx.Message.Equals("Het changeToken verwijst naar een tijdstip vóór het begin van het huidige wijzigingenlogboek.") || serverEx.Message.Equals("The changeToken refers to a time before the start of the current change log.")) && serverEx.ServerErrorCode == Constants.ERROR_CODE_INVALID_CHANGE_TOKEN_TIME)
                    || ((serverEx.Message.Equals("U kunt het changeToken van het ene object niet voor het andere object gebruiken.") || serverEx.Message.Equals("Cannot use the changeToken from one object against a different object")) && serverEx.ServerErrorCode == Constants.ERROR_CODE_INVALID_CHANGE_TOKEN_WRONG_OBJECT))
                    {
                        return new SharePointListItemsDelta(changeTokenInvalid: true);
                    }
                }

                _telemetryClient.TrackException(new CpsException($"Error while getting list changes {siteUrl}", ex));
                throw new CpsException($"Error while getting list changes {siteUrl}");
            }
        }

        #endregion Get changes

        #region Process Changes

        private async Task<ListItemsProcessModel> ProccessListItemsAsync(string siteId, string listId, List<SharePointListItemDelta> items)
        {
            var listItemIds = items.Select(d => d.ListItemId).ToList();
            var response = new ListItemsProcessModel();
            response.processedItemIds = new List<string>();
            response.notProcessedItemIds = new List<string>();
            foreach (var listItemId in listItemIds)
            {
                try
                {
                    // Get ListItem and ids
                    var listItem = await _listRepository.GetListItemAsync(siteId, listId, listItemId.ToString());
                    var ids = await GetObjectIdentifiersAsync(siteId, listId, listItemId.ToString());

                    // Check if listitem must me processed
                    var dropOffMetadata = _metadataRepository.GetDropOffMetadata(listItem, ids);
                    if (!dropOffMetadata.IsComplete || dropOffMetadata.Status == "Verwerkt")
                    {
                        continue;
                    }

                    // Process listitem
                    var isProcessed = await GetMetadataAndProcessListItemAsync(siteId, listId, listItem);
                    if (isProcessed)
                    {
                        response.processedItemIds.Add(listItem.Id);
                    }
                    else
                    {
                        response.notProcessedItemIds.Add(listItem.Id);
                    }
                }
                catch (Exception ex)
                {
                    _telemetryClient.TrackException(new CpsException($"Error while processing listItem {listItemId}", ex));
                    response.notProcessedItemIds.Add(listItemId.ToString());
                }
            }
            return response;
        }

        /// <summary>
        /// - Check if complete
        ///      - IsComplete?
        ///      - All metadata correctly filled?
        /// - Check is listItem is new or update
        /// - Add or update ListItem by our API
        /// - Update listItem in DropOff
        ///      - Succesfull -> Status 'Verwerkt'
        ///      - Unsuccesfull -> Status 'Er gaat iets mis'
        /// </summary>
        private async Task<bool> GetMetadataAndProcessListItemAsync(string siteId, string listId, GraphListItem listItem)
        {
            FileInformation metadata;
            try
            {
                metadata = await GetMetadataAsync(siteId, listId, listItem);
            }
            catch (Exception ex)
            {
                await LogErrorAndSendMailAsync(siteId, listId, listItem, ex, "Failed to get metadata for DropOff file.");
                return false;
            }

            FileInformation newMetadata;
            try
            {
                newMetadata = await GetNewMetadataAsync(metadata);
            }
            catch (Exception ex)
            {
                await LogErrorAndSendMailAsync(siteId, listId, listItem, ex, "Failed to get new metadata for DropOff file.", metadata);
                return false;
            }

            try
            {
                await ProcessListItemAsync(metadata, newMetadata, listItem);
            }
            catch (Exception ex)
            {
                await LogErrorAndSendMailAsync(siteId, listId, listItem, ex, "Failed to process DropOff file.", metadata);
                return false;
            }
            return true;
        }

        private async Task<FileInformation> GetMetadataAsync(string siteId, string listId, GraphListItem listItem)
        {
            var metadata = new FileInformation();
            metadata.Ids = await GetObjectIdentifiersAsync(siteId, listId, listItem.Id);

            // Get new metadata
            // DropOff does not have external references
            return await _metadataRepository.GetMetadataWithoutExternalReferencesAsync(listItem, metadata.Ids);
        }

        private async Task<ObjectIdentifiers> GetObjectIdentifiersAsync(string siteId, string listId, string listItemId)
        {
            var ids = new ObjectIdentifiers();
            ids.SiteId = siteId;
            ids.ListId = listId;
            ids.ListItemId = listItemId;

            // Get all ids for current location
            return await _objectIdRepository.FindMissingIds(ids);
        }

        private async Task<FileInformation> GetNewMetadataAsync(FileInformation metadata)
        {
            var newLocationIds = await GetIdsForNewLocationAsync(metadata);
            var newMetadata = metadata.clone();
            newMetadata.Ids = newLocationIds;
            return newMetadata;
        }

        private async Task<ObjectIdentifiers?> GetIdsForNewLocationAsync(FileInformation metadata)
        {
            var locationMapping = _globalSettings.LocationMapping.Find(item =>
                item.Classification.Equals(metadata.AdditionalMetadata.Classification, StringComparison.OrdinalIgnoreCase)
                && item.Source.Equals(metadata.AdditionalMetadata.Source, StringComparison.OrdinalIgnoreCase)
            );
            if (locationMapping == null)
            {
                return null;
            }

            var newLocationIds = new ObjectIdentifiers();
            newLocationIds.SiteId = locationMapping.SiteId;
            newLocationIds.ListId = locationMapping.ListId;
            newLocationIds.ExternalReferenceListId = locationMapping.ExternalReferenceListId;
            newLocationIds.ObjectId = metadata.Ids.ObjectId;
            if (newLocationIds.ObjectId != null)
            {
                newLocationIds = await _objectIdRepository.FindMissingIds(newLocationIds);
            }
            return newLocationIds;
        }

        private async Task ProcessListItemAsync(FileInformation metadata, FileInformation newMetadata, GraphListItem listItem)
        {
            // Get new content
            var stream = await _driveRepository.GetStreamAsync(metadata.Ids.DriveId, metadata.Ids.DriveItemId);

            // Create or update file metadata and content
            metadata.Ids.ObjectId = await CreateOrUpdateFileAsync(newMetadata, stream);

            // Succesfull -> change status
            await _metadataRepository.UpdateDropOffMetadataAsync(true, "Verwerkt", metadata);

            // Mail proccessed file.
            await _emailService.GetEmailAndSendMailAsync("DropOff Bestand succesvol verwerkt: " + metadata.Ids.ObjectId, $"Het bestand \"{metadata.FileName}\" is succesvol verwerkt en nu beschikbaar op de doellocatie", listItem);
        }

        private async Task<string> CreateOrUpdateFileAsync(FileInformation metadata, Stream stream)
        {
            var isNewFile = metadata.Ids.ObjectId == null;
            if (isNewFile)
            {
                var spoIds = await _filesRepository.CreateFileByStreamAsync(metadata, stream);
                return spoIds.ObjectId;
            }
            else
            {
                await _metadataRepository.UpdateAllMetadataAsync(metadata);
                await _filesRepository.UpdateContentAsync(metadata.Ids.ObjectId, stream);
                return metadata.Ids.ObjectId;
            }
        }

        private string GetNotificationProcessSummary(ListItemsProcessModel processedItems)
        {
            var message = "Notification proccesed.";
            message += $" Found {processedItems.processedItemIds.Count + processedItems.notProcessedItemIds.Count} items, ";
            message += $" {processedItems.processedItemIds.Count} items successfully processed, ";
            message += $" {processedItems.notProcessedItemIds.Count} items not processed ({String.Join(", ", processedItems.notProcessedItemIds)})";
            return message;
        }

        #endregion Process Changes

        #region Error logging

        private async Task LogErrorAndSendMailAsync(string siteId, string listId, GraphListItem listItem, Exception ex, string errorMessage, FileInformation? metadata = null)
        {
            // Unsuccesfull -> change status
            if (metadata != null)
            {
                await _metadataRepository.UpdateDropOffMetadataAsync(false, "Er gaat iets mis", metadata);
            }

            // Log error, we need the metadata to update the error in the DropOff.
            LogError(siteId, listId, listItem.Id, ex, errorMessage);

            // Mail not proccessed file.
            var fileIdentifier = metadata == null ? listItem.Id : metadata.FileName;
            await _emailService.GetEmailAndSendMailAsync("DropOff foutmelding", $"Er is iets mis gegaan bij het verwerken van het bestand \"{fileIdentifier}\".", listItem);
        }

        private void LogError(string siteId, string listId, string listItemId, Exception ex, string errorMessage)
        {
            var properties = new Dictionary<string, string>
            {
                ["siteId"] = siteId,
                ["listId"] = listId,
                ["listItemId"] = listItemId
            };
            _telemetryClient.TrackException(new CpsException(errorMessage, ex), properties);
        }

        #endregion Error logging

        #endregion Handle Notification
    }
}