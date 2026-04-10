using CPS_API.Database;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Repositories;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SharePoint.Client;
using GraphListItem = Microsoft.Graph.Models.ListItem;
using WebhookType = CPS_API.Models.WebhookType;

namespace CPS_API.Services
{
    public interface IWebhookNotificationService
    {
        Task<string> HandleWebhookNotificationFromQueueAsync(WebHookNotification notification);
    }

    public class WebhookNotificationService : IWebhookNotificationService
    {
        private readonly IListRepository _listRepository;
        private readonly IMetadataRepository _metadataRepository;
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly IDriveRepository _driveRepository;
        private readonly IFilesRepository _filesRepository;
        private readonly ISharePointRepository _sharePointRepository;
        private readonly IExportRepository _exportRepository;
        private readonly IWebHookRepository _webhookRepository;
        private readonly EmailService _emailService;
        private readonly GlobalSettings _globalSettings;
        private readonly TelemetryClient _telemetryClient;
        private readonly CpsDbContext _dbContext;
        private readonly IDatabaseHealthService _databaseHealthService;
        private readonly ILogger _logger;

        public WebhookNotificationService(IListRepository listRepository, IMetadataRepository metadataRepository, IObjectIdRepository objectIdRepository, IDriveRepository driveRepository, IFilesRepository filesRepository, ISharePointRepository sharePointRepository, IExportRepository exportRepository, IWebHookRepository webhookRepository, EmailService emailService, IOptions<GlobalSettings> globalSettings, TelemetryClient telemetryClient, CpsDbContext dbContext, IDatabaseHealthService databaseHealthService, ILogger<WebHookRepository> logger)//NOSONAR
        {
            _listRepository = listRepository;
            _metadataRepository = metadataRepository;
            _objectIdRepository = objectIdRepository;
            _driveRepository = driveRepository;
            _filesRepository = filesRepository;
            _sharePointRepository = sharePointRepository;
            _exportRepository = exportRepository;
            _webhookRepository = webhookRepository;
            _emailService = emailService;
            _globalSettings = globalSettings.Value;
            _telemetryClient = telemetryClient;
            _dbContext = dbContext;
            _databaseHealthService = databaseHealthService;
            _logger = logger;
        }

        /// <summary>
        /// Handle the webhook notification from a queue.
        ///  - Get the changes in list from the notification.
        ///  - Process the changes
        ///   - Handle PublicSync Notification
        ///     - Check if document must me processed
        ///     - Add or update the document in FileStorage
        ///   - Handle DropOff Notification
        ///     - Check if document must me processed
        ///     - Add or update the document in the right list
        ///     - Set status in DropOff to processed
        ///  - Keep last changetoken for next notification
        /// </summary>
        public async Task<string> HandleWebhookNotificationFromQueueAsync(WebHookNotification notification)
        {
            var site = await _sharePointRepository.GetSiteByRelativeUrlAsync(notification.SiteUrl);
            if (string.IsNullOrWhiteSpace(site.Id) || string.IsNullOrWhiteSpace(site.WebUrl)) throw new CpsException($"Error while getting site (SiteUrl = {notification.SiteUrl})");

            // Get changes
            var webhookSubscription = await GetWebhookSubscriptionAsync(notification.SubscriptionId);

            if (string.IsNullOrWhiteSpace(webhookSubscription.LastChangeToken))
            {
                _telemetryClient.TrackTrace("Change token is empty, attempting to retrieve whole change history.");
            }
            var changes = await _listRepository.GetListAndChangesAsync(site.WebUrl, notification.Resource, webhookSubscription.LastChangeToken);

            // Process the changes
            string response;
            if (webhookSubscription.WebhookType == WebhookType.PublicSync)
            {
                var syncResponse = await _exportRepository.SynchroniseFoundDocumentsAsync(changes, site.Id, notification.Resource);
                response = GetSyncResponse(syncResponse);
            }
            else
            {
                // Remove deleted items and keep unique changes
                var deletedItemIds = changes.Items.Where(d => d.ChangeType == ChangeType.DeleteObject).DistinctBy(d => d.ListItemId).Select(d => d.ListItemId).ToList();
                changes.Items = changes.Items.DistinctBy(d => d.ListItemId).ToList();
                changes.Items = changes.Items.Where(d => !deletedItemIds.Contains(d.ListItemId)).ToList();

                var processedItems = await ProccessListItemsAsync(site.Id, notification.Resource, changes.Items);
                response = GetDropOffResponse(processedItems, site.Id, notification.Resource);
            }

            // Save the last changetoken for next notification
            await UpdateLastChangeTokenAsync(notification.SubscriptionId, changes.NewChangeToken);

            // Extend the webhook when needed
            // If the webhook is about to expire within the coming 7 days then prolong it
            if (notification.ExpirationDateTime.AddDays(-7) < DateTime.Now)
            {
                await _webhookRepository.ExtendWebHookAsync(site.WebUrl, notification.Resource, notification.SubscriptionId);
            }

            return response;
        }

        private async Task<WebhookSubscription> GetWebhookSubscriptionAsync(string subscriptionId)
        {
            var webhookSubscription = await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.WebhookSubscription.FirstOrDefaultAsync(ws => ws.SubscriptionId.Equals(subscriptionId)),
                nameof(GetWebhookSubscriptionAsync)
            );
            if (webhookSubscription == null) throw new CpsException($"Error while getting webhook subscription (subscriptionId = {subscriptionId})");
            return webhookSubscription;
        }

        private static string GetSyncResponse(ExportResponse result)
        {
            var failedItemsStr = result.FailedItemIds.Select(item => $"Error while adding file (ListItemId: {item}) to FileStorage.\r\n").ToList();
            var failedItemsMessage = String.Join(",", failedItemsStr.Select(x => x.ToString()).ToArray());

            var message = $"Notification proccesed. SiteId: {result.SiteId}, ListId: {result.ListId}:";
            message += $" Found {result.AddedItemIds.Count + result.UpdatedItemIds.Count + result.DeletedItemIds.Count} items, ";
            message += $" {result.AddedItemIds.Count} items added, ";
            message += $" {result.UpdatedItemIds.Count} items updated, ";
            message += $" {result.DeletedItemIds.Count} items deleted ";
            message += (failedItemsStr.Count == 0 ? "" : "\r\n") + failedItemsMessage;
            return message;
        }

        private static string GetDropOffResponse(ListItemsProcessModel processedItems, string siteId, string listId)
        {
            var message = $"Notification proccesed. SiteId: {siteId}, ListId: {listId}:";
            message += $" Found {processedItems.processedItemIds.Count + processedItems.notProcessedItemIds.Count} items, ";
            message += $" {processedItems.processedItemIds.Count} items successfully processed, ";
            message += $" {processedItems.notProcessedItemIds.Count} items not processed ({String.Join(", ", processedItems.notProcessedItemIds)})";
            return message;
        }

        /// <summary>
        /// Process documents from DropOff
        ///  - Check if document is complete for processing
        ///  - Add or update the document in the right list
        /// </summary>
        private async Task<ListItemsProcessModel> ProccessListItemsAsync(string siteId, string listId, List<SharePointListItemDelta> items)
        {
            ListItemsProcessModel response = new();
            response.processedItemIds = [];
            response.notProcessedItemIds = [];

            var itemIds = items.Select(item => item.ListItemId);
            foreach (var listItemId in itemIds)
            {
                try
                {
                    // Get ListItem and ids
                    var listItem = await _listRepository.GetListItemAsync(siteId, listId, listItemId.ToString());
                    if (listItem == null)
                    {
                        response.notProcessedItemIds.Add(listItemId.ToString());
                        continue;
                    }

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
                        response.processedItemIds.Add(listItemId.ToString());
                    }
                    else
                    {
                        response.notProcessedItemIds.Add(listItemId.ToString());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing listItem {ListItemId}", listItemId);
                    response.notProcessedItemIds.Add(listItemId.ToString());
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
            if (string.IsNullOrWhiteSpace(listItem.Id)) throw new CpsException($"Error while processing listItem: listItem ID unknown");

            // DropOff document metadata
            // DropOff does not have external references
            FileInformation metadata;
            try
            {
                var ids = await _objectIdRepository.GetObjectIdentifiersAsync(siteId, listId, listItem.Id);
                metadata = await _metadataRepository.GetMetadataWithoutExternalReferencesAsync(listItem, ids, getObjectId: true);
            }
            catch (Exception ex)
            {
                await LogErrorAndSendMailAsync(siteId, listId, listItem, "Failed to get metadata for DropOff file.", ex);
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
                await LogErrorAndSendMailAsync(siteId, listId, listItem, "Failed to get new metadata for DropOff file.", ex, metadata);
                return false;
            }
            if (metadata.Ids == null || string.IsNullOrEmpty(metadata.Ids.DriveId) || string.IsNullOrEmpty(metadata.Ids.DriveItemId))
            {
                await LogErrorAndSendMailAsync(siteId, listId, listItem, "Failed to process DropOff file.", metadata: metadata);
                return false;
            }

            try
            {
                var stream = await _driveRepository.GetStreamAsync(metadata.Ids.DriveId!, metadata.Ids.DriveItemId!);
                metadata.Ids.ObjectId = await CreateOrUpdateFileAsync(newMetadata, stream);
                await _metadataRepository.UpdateDropOffMetadataAsync(true, "Verwerkt", metadata.Ids);
            }
            catch (Exception ex)
            {
                await LogErrorAndSendMailAsync(siteId, listId, listItem, "Failed to process DropOff file.", ex, metadata);
                return false;
            }

            _emailService.GetAuthorEmailAndSendMailAsync("DropOff Bestand succesvol verwerkt: " + metadata.Ids.ObjectId, $"Het bestand \"{metadata.FileName}\" is succesvol verwerkt en nu beschikbaar op de doellocatie", listItem);
            return true;
        }

        /// <summary>
        /// Classification and source determine new location for DropOff document.
        /// </summary>
        private async Task<ObjectIdentifiers?> GetIdsForNewLocationAsync(FileInformation metadata)
        {
            if (metadata.AdditionalMetadata == null) throw new CpsException($"No {nameof(FileInformation.AdditionalMetadata)} found for {nameof(metadata)}");
            if (metadata.Ids == null) throw new CpsException($"No {nameof(FileInformation.Ids)} found for {nameof(metadata)}");

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
            newLocationIds.ObjectId = metadata.Ids.ObjectId;
            if (newLocationIds.ObjectId != null)
            {
                newLocationIds = await _objectIdRepository.FindMissingIds(newLocationIds);
            }
            return newLocationIds;
        }

        private async Task<string> CreateOrUpdateFileAsync(FileInformation metadata, Stream stream)
        {
            if (metadata.Ids == null) throw new CpsException($"No {nameof(FileInformation.Ids)} found for {nameof(metadata)}");

            var isNewFile = string.IsNullOrWhiteSpace(metadata.Ids.ObjectId);
            if (isNewFile)
            {
                var spoIds = await _filesRepository.CreateFileByStreamAsync(metadata, stream);
                return spoIds.ObjectId;
            }
            else
            {
                await _metadataRepository.UpdateAllMetadataAsync(metadata);
                await _filesRepository.UpdateContentAsync(metadata.Ids.ObjectId!, stream);
                return metadata.Ids.ObjectId!;
            }
        }

        private async Task UpdateLastChangeTokenAsync(string subscriptionId, string lastChangeToken)
        {
            var webhookSubscription = await GetWebhookSubscriptionAsync(subscriptionId);
            webhookSubscription.LastChangeToken = lastChangeToken;

            await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.SaveChangesAsync(),
                nameof(GetWebhookSubscriptionAsync)
            );
        }

        #region Error logging

        private async Task LogErrorAndSendMailAsync(string siteId, string listId, GraphListItem listItem, string errorMessage, Exception? ex = null, FileInformation? metadata = null)
        {
            // Unsuccesfull -> change status
            if (metadata != null)
            {
                if (metadata.Ids == null) throw new CpsException($"No {nameof(FileInformation.Ids)} found for {nameof(metadata)}");
                await _metadataRepository.UpdateDropOffMetadataAsync(false, "Er gaat iets mis", metadata.Ids);
            }

            // Log error, we need the metadata to update the error in the DropOff.
            if (string.IsNullOrWhiteSpace(listItem.Id)) throw new CpsException($"Error while logging error: listItem ID unknown");
            LogError(siteId, listId, listItem.Id, errorMessage, ex);

            // Mail not proccessed file.
            var fileIdentifier = metadata == null ? listItem.Id : metadata.FileName;
            _emailService.GetAuthorEmailAndSendMailAsync("DropOff foutmelding", $"Er is iets mis gegaan bij het verwerken van het bestand \"{fileIdentifier}\".", listItem);
        }

        private void LogError(string siteId, string listId, string listItemId, string errorMessage, Exception? ex = null)
        {
            var properties = new Dictionary<string, string>
            {
                ["siteId"] = siteId,
                ["listId"] = listId,
                ["listItemId"] = listItemId
            };
            _logger.LogError(ex ?? new Exception(errorMessage), errorMessage, properties);
        }

        #endregion Error logging
    }
}