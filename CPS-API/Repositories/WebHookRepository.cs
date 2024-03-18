using System.Net.Http.Headers;
using System.Text;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Constants = CPS_API.Models.Constants;
using GraphListItem = Microsoft.Graph.ListItem;
using GraphSite = Microsoft.Graph.Site;

namespace CPS_API.Repositories
{
    public interface IWebHookRepository
    {
        Task<SubscriptionModel> CreateWebHookForDropOffAsync();

        Task<DateTime?> ExtendWebHookForDropOffAsync();

        Task<bool> DeleteWebHookAsync(GraphSite site, string listId, string subscriptionId);

        Task<string?> HandleSharePointNotificationAsync(string? validationToken, ResponseModel<WebHookNotification>? notificationsResponse);

        Task<ListItemsProcessModel> HandleDropOffNotificationAsync(WebHookNotification notification);
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
        private readonly CertificateService _certificateService;

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
            CertificateService certificateService,
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
            _certificateService = certificateService;
            _emailService = emailService;
            _globalSettings = settings.Value;
            _telemetryClient = telemetryClient;
        }

        #region Create/Edit/Delete Webhook

        public async Task<SubscriptionModel> CreateWebHookForDropOffAsync()
        {
            var site = await _sharePointRepository.GetSiteAsync(_globalSettings.WebHookSettings.SiteId);
            if (site == null)
            {
                throw new CpsException("Error while getting site");
            }

            var certificate = await _certificateService.GetCertificateAsync();
            using var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, certificate, _globalSettings.TenantId);
            var accessToken = await authenticationManager.GetAccessTokenAsync(site.WebUrl);
            if (accessToken == null)
            {
                throw new CpsException("Error while getting accessToken");
            }

            var subscription = await AddListWebHookAsync(site.WebUrl, _globalSettings.WebHookSettings.ListId, _globalSettings.WebHookSettings.EndPoint, accessToken, _globalSettings.WebHookSettings.ClientState);
            if (subscription == null)
            {
                throw new CpsException("Error while adding webhook");
            }

            // Save expiration date and subscriptionId for extending webhook.
            await _settingsRepository.SaveSettingAsync(Constants.DropOffSubscriptionId, subscription.Id);
            await _settingsRepository.SaveSettingAsync(Constants.DropOffSubscriptionExpirationDateTime, subscription.ExpirationDateTime);

            return subscription;
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

        public async Task<DateTime?> ExtendWebHookForDropOffAsync()
        {
            var site = await _sharePointRepository.GetSiteAsync(_globalSettings.WebHookSettings.SiteId);
            if (site == null)
            {
                throw new CpsException("Error while getting site");
            }

            var certificate = await _certificateService.GetCertificateAsync();
            using var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, certificate, _globalSettings.TenantId);
            var accessToken = await authenticationManager.GetAccessTokenAsync(site.WebUrl);
            if (accessToken == null)
            {
                throw new CpsException("Error while getting accessToken");
            }

            var subscriptionId = await _settingsRepository.GetSetting<string>(Constants.DropOffSubscriptionId);
            var expirationDateTime = await UpdateListWebHookAsync(site.WebUrl, _globalSettings.WebHookSettings.ListId, subscriptionId, _globalSettings.WebHookSettings.EndPoint, accessToken, _globalSettings.WebHookSettings.ClientState);
            if (expirationDateTime == null)
            {
                throw new CpsException("Error while adding webhook");
            }

            // Save expiration date for renewing webhook.
            await _settingsRepository.SaveSettingAsync(Constants.DropOffSubscriptionExpirationDateTime, expirationDateTime);

            return expirationDateTime;
        }

        /// <summary>
        /// Updates the expiration datetime (and notification URL) of an existing SharePoint list web hook
        /// </summary>
        /// <param name="siteUrl">Url of the site holding the list</param>
        /// <param name="listId">Id of the list</param>
        /// <param name="subscriptionId">Id of the web hook subscription that we need to update</param>
        /// <param name="webHookEndPoint">Url of the web hook service endpoint (the one that will be called during an event)</param>
        /// <param name="accessToken">Access token to authenticate against SharePoint</param>
        /// <returns>true if succesful, exception in case something went wrong</returns>
        /// <param name="validityInMonths">Optional web hook validity in months, defaults to 3 months, max is 6 months</param>
        public async Task<DateTime?> UpdateListWebHookAsync(string siteUrl, string listId, string subscriptionId, string webHookEndPoint, string accessToken, string webHookClientState, int validityInMonths = 3)
        {
            using (var httpClient = new HttpClient())
            {
                string requestUrl = String.Format("{0}/_api/web/lists('{1}')/subscriptions('{2}')", siteUrl, listId, subscriptionId);
                HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var expirationDateTime = DateTime.Now.AddMonths(validityInMonths).ToUniversalTime();
                request.Content = new StringContent(JsonConvert.SerializeObject(
                    new SubscriptionModel()
                    {
                        ExpirationDateTime = expirationDateTime,
                        NotificationUrl = webHookEndPoint,
                        ClientState = webHookClientState
                    }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.SendAsync(request);

                if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
                {
                    // oops...something went wrong, maybe the web hook does not exist?
                    throw new Exception(await response.Content.ReadAsStringAsync());
                }
                else
                {
                    return await Task.Run(() => expirationDateTime);
                }
            }
        }

        public async Task<bool> DeleteWebHookAsync(GraphSite site, string listId, string subscriptionId)
        {
            var certificate = await _certificateService.GetCertificateAsync();
            using var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, certificate, _globalSettings.TenantId);
            var accessToken = await authenticationManager.GetAccessTokenAsync(site.WebUrl);
            if (accessToken == null)
            {
                throw new CpsException("Error while deleting accessToken");
            }

            return await DeleteListWebHookAsync(site.WebUrl, listId, subscriptionId, accessToken);
        }

        /// <summary>
        /// Deletes an existing SharePoint list web hook
        /// </summary>
        /// <param name="siteUrl">Url of the site holding the list</param>
        /// <param name="listId">Id of the list</param>
        /// <param name="subscriptionId">Id of the web hook subscription that we need to delete</param>
        /// <param name="accessToken">Access token to authenticate against SharePoint</param>
        /// <returns>true if succesful, exception in case something went wrong</returns>
        public async Task<bool> DeleteListWebHookAsync(string siteUrl, string listId, string subscriptionId, string accessToken)
        {
            using (var httpClient = new HttpClient())
            {
                string requestUrl = String.Format("{0}/_api/web/lists('{1}')/subscriptions('{2}')", siteUrl, listId, subscriptionId);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, requestUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                HttpResponseMessage response = await httpClient.SendAsync(request);

                if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
                {
                    // oops...something went wrong, maybe the web hook does not exist?
                    throw new Exception(await response.Content.ReadAsStringAsync());
                }
                else
                {
                    return await Task.Run(() => true);
                }
            }
        }

        #endregion Create/Edit/Delete Webhook

        #region Handle SharePoint Notification

        /// <summary>
        /// When adding a webhook respond with given validation token.
        /// 
        /// Webhook sends notification when something changes in the list.
        /// Put this notification on a queue, so response time remains within 5 seconds.
        /// </summary>
        public async Task<string?> HandleSharePointNotificationAsync(string? validationToken, ResponseModel<WebHookNotification>? notificationsResponse)
        {
            _telemetryClient.TrackTrace($"Webhook endpoint triggered!");

            // If a validation token is present, we need to respond within 5 seconds by
            // returning the given validation token. This only happens when a new
            // webhook is being added
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

        #endregion Handle Notification

        #region Handle DropOff Notification

        /// <summary>
        /// Handle the webhook notification from a queue.
        ///  - Get the changes in list from the notification.
        ///  - Process the changes
        ///     - Check if document must me processed
        ///     - Add or update the document in the right list
        ///     - Set status in DropOff to processed
        ///  - Keep last changetoken for next notification
        /// </summary>
        public async Task<ListItemsProcessModel> HandleDropOffNotificationAsync(WebHookNotification notification)
        {
            var site = await _sharePointRepository.GetSiteAsync(_globalSettings.HostName + ":" + notification.SiteUrl + ":/");

            // Get changes
            var changeToken = await _settingsRepository.GetSetting<string>(Constants.DropOffLastChangeToken);
            if (string.IsNullOrWhiteSpace(changeToken))
            {
                _telemetryClient.TrackTrace("Change token is empty, attempting to retrieve whole change history.");
            }
            var changes = await _listRepository.GetListAndFilteredChangesAsync(site.WebUrl, notification.Resource, changeToken);

            // Process the changes
            var processedItems = await ProccessListItemsAsync(site.Id, notification.Resource, changes.Items);
            await _settingsRepository.SaveSettingAsync(Constants.DropOffLastChangeToken, changes.NewChangeToken);

            // Extend the webhook when needed
            // If the webhook is about to expire within the coming 7 days then prolong it
            if (notification.ExpirationDateTime.AddDays(-7) < DateTime.Now)
            {
                await ExtendWebHookForDropOffAsync();
            }

            return processedItems;
        }

        #region Process Changes

        /// <summary>
        /// Process documents from DropOff
        ///  - Check if document is complete for processing
        ///  - Add or update the document in the right list
        /// </summary>
        private async Task<ListItemsProcessModel> ProccessListItemsAsync(string siteId, string listId, List<SharePointListItemDelta> items)
        {
            var response = new ListItemsProcessModel();
            response.processedItemIds = new List<string>();
            response.notProcessedItemIds = new List<string>();

            foreach (var item in items)
            {
                try
                {
                    // Get ListItem and ids
                    var listItem = await _listRepository.GetListItemAsync(siteId, listId, item.ListItemId.ToString());
                    var ids = await _objectIdRepository.GetObjectIdentifiersAsync(siteId, listId, item.ListItemId.ToString());

                    // Check if listitem must me processed
                    var dropOffMetadata = _metadataRepository.GetDropOffMetadata(listItem);
                    if (!dropOffMetadata.IsComplete || (dropOffMetadata.Status != null && dropOffMetadata.Status.Equals(Constants.DropOffMetadataStatusProcessed, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        continue;
                    }

                    // Process listitem
                    var isProcessed = await ProcessListItemAndSendMailAsync(siteId, listId, listItem);
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
                    _telemetryClient.TrackException(new CpsException($"Error while processing listItem {item.ListItemId}", ex));
                    response.notProcessedItemIds.Add(item.ListItemId.ToString());
                }
            }
            return response;
        }

        /// <summary>
        /// - Create or update the document in the right list
        /// - Update document in DropOff
        ///      - Succesfull -> Status 'Verwerkt'
        ///      - Unsuccesfull -> Status 'Er gaat iets mis'
        /// - Send mail to creater
        /// </summary>
        private async Task<bool> ProcessListItemAndSendMailAsync(string siteId, string listId, GraphListItem listItem)
        {
            // DropOff document metadata
            // DropOff does not have external references
            FileInformation metadata;
            try
            {
                var ids = await _objectIdRepository.GetObjectIdentifiersAsync(siteId, listId, listItem.Id);
                metadata = await _metadataRepository.GetMetadataWithoutExternalReferencesAsync(listItem, ids);
            }
            catch (Exception ex)
            {
                await LogErrorAndSendMailAsync(siteId, listId, listItem, ex, "Failed to get metadata for DropOff file.");
                return false;
            }

            // New location document metadata
            FileInformation newMetadata;
            try
            {
                newMetadata = metadata.clone();
                newMetadata.Ids = await GetIdsForNewLocationAsync(metadata);
            }
            catch (Exception ex)
            {
                await LogErrorAndSendMailAsync(siteId, listId, listItem, ex, "Failed to get new metadata for DropOff file.", metadata);
                return false;
            }

            try
            {
                var stream = await _driveRepository.GetStreamAsync(metadata.Ids.DriveId, metadata.Ids.DriveItemId);
                metadata.Ids.ObjectId = await CreateOrUpdateFileAsync(newMetadata, stream);
                await _metadataRepository.UpdateDropOffMetadataAsync(true, "Verwerkt", metadata);
            }
            catch (Exception ex)
            {
                await LogErrorAndSendMailAsync(siteId, listId, listItem, ex, "Failed to process DropOff file.", metadata);
                return false;
            }

            await _emailService.GetAuthorEmailAndSendMailAsync("DropOff Bestand succesvol verwerkt: " + metadata.Ids.ObjectId, $"Het bestand \"{metadata.FileName}\" is succesvol verwerkt en nu beschikbaar op de doellocatie", listItem);
            return true;
        }

        /// <summary>
        /// Classification and source determine new location for DropOff document.
        /// </summary>
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
            await _emailService.GetAuthorEmailAndSendMailAsync("DropOff foutmelding", $"Er is iets mis gegaan bij het verwerken van het bestand \"{fileIdentifier}\".", listItem);
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

        #endregion Handle DropOff Notification
    }
}