using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Options;
using Microsoft.SharePoint.Client;

namespace CPS_API.Repositories
{
    public interface IExportRepository
    {
        Task<ToBePublishedExportResponse> SynchroniseToBePublishedDocumentsAsync();

        Task<ExportResponse> SynchroniseFoundDocumentsAsync(SharePointListItemsDelta changes, string siteId, string listId);
    }

    public class ExportRepository : IExportRepository
    {
        private readonly IDriveRepository _driveRepository;
        private readonly IMetadataRepository _metadataRepository;
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly IPublicationRepository _publicationRepository;
        private readonly ICallbackRepository _callbackRepository;

        private readonly FileStorageService _fileStorageService;
        private readonly XmlExportSerivce _xmlExportSerivce;

        private readonly GlobalSettings _globalSettings;

        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger _logger;

        public ExportRepository(IDriveRepository driveRepository, IMetadataRepository metadataRepository, IObjectIdRepository objectIdRepository, IPublicationRepository publicationRepository, ICallbackRepository callbackRepository, FileStorageService fileStorageService, XmlExportSerivce xmlExportSerivce, IOptions<GlobalSettings> settings, TelemetryClient telemetryClient, ILogger<ExportRepository> logger)//NOSONAR
        {
            _driveRepository = driveRepository;
            _metadataRepository = metadataRepository;
            _objectIdRepository = objectIdRepository;
            _publicationRepository = publicationRepository;
            _callbackRepository = callbackRepository;
            _fileStorageService = fileStorageService;
            _xmlExportSerivce = xmlExportSerivce;
            _globalSettings = settings.Value;
            _telemetryClient = telemetryClient;
            _logger = logger;
        }

        /// <summary>
        /// For each file:
        ///  - Generate xml from metadata
        ///  - Upload file to storage container
        ///  - Upload xml to storage container
        ///  - Post new file to callback
        ///  
        /// Check for to be published documents
        ///  - Synchronise each to be published file
        /// </summary>
        public async Task<ExportResponse> SynchroniseFoundDocumentsAsync(SharePointListItemsDelta changes, string siteId, string listId)
        {
            var groupedChanges = changes.Items.GroupBy(item => item.ListItemId);

            var notSyncedItemIds = new List<int>();
            var addedItemIds = new List<int>();
            var updatedItemIds = new List<int>();
            var deletedItemIds = new List<int>();
            foreach (var groupedChange in groupedChanges)
            {
                if (groupedChange == null)
                {
                    continue;
                }

                var listItemId = groupedChange.Key;
                var items = groupedChange.ToList();

                var synchronisationType = GetSynchronisationType(items);
                if (synchronisationType == null)
                {
                    continue;
                }

                ObjectIdentifiers? objectIdentifiers = await TryGetObjectIdentifiersAsync(siteId, listId, listItemId, notSyncedItemIds);
                if (objectIdentifiers == null)
                {
                    continue;
                }

                await TrySynchroniseDocumentsAsync(objectIdentifiers, synchronisationType.Value, notSyncedItemIds, addedItemIds, updatedItemIds, deletedItemIds, listItemId);
            }

            return new ExportResponse(siteId, listId, changes.NewChangeToken, notSyncedItemIds, addedItemIds, updatedItemIds, deletedItemIds);
        }

        private static SynchronisationType? GetSynchronisationType(List<SharePointListItemDelta> items)
        {
            // One version is deleted? Then the file is deleted
            // When one version is an add action, we need to add the newest version.
            // When there are only updated actions, then we need to update the file to the newest version.
            if (items.Exists(item => item.ChangeType == ChangeType.DeleteObject || item.ChangeType == ChangeType.MoveAway))
                return SynchronisationType.delete;
            if (items.Exists(item => item.ChangeType == ChangeType.Add || item.ChangeType == ChangeType.MoveInto))
                return SynchronisationType.create;
            if (items.Exists(item => item.ChangeType == ChangeType.Update || item.ChangeType == ChangeType.Rename))
                return SynchronisationType.update;
            return null;
        }

        private async Task<ObjectIdentifiers?> TryGetObjectIdentifiersAsync(string siteId, string listId, int listItemId, List<int> notSyncedItemIds)
        {
            try
            {
                return await _objectIdRepository.GetObjectIdentifiersBySharePointIdsAsync(siteId, listId, listItemId.ToString());
            }
            catch (Exception ex)
            {
                notSyncedItemIds.Add(listItemId);
                TrackCpsException(ex, siteId, listId, listItemId.ToString(), errorMessage: "Error while getting objectIdentifiers: " + ex.Message);
                _telemetryClient.TrackTrace("New document synchronisation failed", new Dictionary<string, string>
                {
                    { nameof(siteId), siteId },
                    { nameof(listId), listId },
                    { nameof(listItemId), listItemId.ToString() },
                    { "ErrorMessage", ex.Message }
                });
                return null;
            }
        }

        private async Task TrySynchroniseDocumentsAsync(
            ObjectIdentifiers objectIdentifiers,
            SynchronisationType synchronisationType,
            List<int> notSyncedItemIds,
            List<int> addedItemIds,
            List<int> updatedItemIds,
            List<int> deletedItemIds,
            int listItemId)
        {
            try
            {
                var succeeded = await SynchroniseDocumentsAsync(objectIdentifiers, synchronisationType);
                if (succeeded)
                {
                    switch (synchronisationType)
                    {
                        case SynchronisationType.create:
                            addedItemIds.Add(listItemId);
                            break;
                        case SynchronisationType.update:
                            updatedItemIds.Add(listItemId);
                            break;
                        case SynchronisationType.delete:
                            deletedItemIds.Add(listItemId);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                notSyncedItemIds.Add(listItemId);
                TrackCpsException(ex, objectIdentifiers.SiteId, objectIdentifiers.ListId, objectIdentifiers.ListItemId, objectIdentifiers.ObjectId, "Error while synchronising new documents");
                _telemetryClient.TrackTrace("New document synchronisation failed", new Dictionary<string, string>
                {
                    { nameof(objectIdentifiers.ObjectId), objectIdentifiers.ObjectId },
                    { nameof(objectIdentifiers.ListItemId), objectIdentifiers.ListItemId },
                    { "ErrorMessage", ex.Message }
                });
            }
        }

        /// <summary>
        /// For each file:
        ///  - Generate xml from metadata
        ///  - Upload file to storage container
        ///  - Upload xml to storage container
        ///  - Post new file to callback
        ///  
        /// Check for to be published documents
        ///  - Synchronise each to be published file
        /// </summary>
        public async Task<ToBePublishedExportResponse> SynchroniseToBePublishedDocumentsAsync()
        {
            // Check for to be published documents and synchronise.
            List<ToBePublished> items = await _publicationRepository.GetItemsFromQueueAsync();
            items = [.. items.Where(item => !item.PublicationDate.IsDateInFuture())];

            var itemsAdded = 0;
            var failedToBePublishedItems = new List<ToBePublished>();
            foreach (var item in items)
            {
                ObjectIdentifiers? objectIdentifiers;
                try
                {
                    objectIdentifiers = await GetObjectIdentifiersAsync(item.ObjectId);
                }
                catch (Exception ex)
                {
                    failedToBePublishedItems.Add(item);
                    TrackCpsException(ex, objectId: item.ObjectId, errorMessage: "Error while getting objectIdentifiers: " + ex.Message);
                    _telemetryClient.TrackTrace($"New document synchronisation failed", new Dictionary<string, string>
                    {
                        { nameof(item.ObjectId), item.ObjectId },
                        { "ErrorMessage", ex.Message }
                    });
                    continue;
                }

                try
                {
                    var succeeded = await SynchroniseDocumentsAsync(objectIdentifiers, SynchronisationType.create);
                    if (succeeded)
                    {
                        await _publicationRepository.RemoveFromQueueAsync(item);
                        itemsAdded++;
                    }
                }
                catch (Exception ex)
                {
                    failedToBePublishedItems.Add(item);
                    TrackCpsException(ex, objectIdentifiers.DriveId, objectIdentifiers.DriveItemId, objectIdentifiers.ObjectId, Constants.NewDocumentsSynchronisationError);
                    _telemetryClient.TrackTrace("New document synchronisation failed", new Dictionary<string, string>
                    {
                        { nameof(objectIdentifiers.ObjectId), objectIdentifiers.ObjectId },
                        { nameof(objectIdentifiers.DriveItemId), objectIdentifiers.DriveItemId },
                        { "ErrorMessage", ex.Message }
                    });
                }
            }

            return new ToBePublishedExportResponse(itemsAdded, failedToBePublishedItems);
        }

        private async Task<(bool, string)> UploadFileAndXmlToFileStorageAsync(ObjectIdentifiers objectIdentifiers)
        {
            // When metadata is unknown, we skip the synchronisation.
            // The file is a new incomplete file or something went wrong while adding the file.
            var metadataExists = await _metadataRepository.FileContainsMetadata(objectIdentifiers);
            if (!metadataExists)
            {
                return (false, "Metadata is incomplete");
            }

            var metadata = await GetMetadataAsync(objectIdentifiers.ObjectId);

            // Skip document when it is not ready for publishing.
            var isReadyForPublishing = await CheckPublicationDateAsync(metadata, objectIdentifiers.ObjectId);
            if (!isReadyForPublishing)
            {
                return (false, "Document is not ready for publishing");
            }

            var stream = await GetStreamAsync(objectIdentifiers);
            await CreateContentAsync(objectIdentifiers, metadata, stream);

            var metadataXml = GetMetadataAsXml(metadata);
            await CreateMetadataXmlAsync(objectIdentifiers, metadataXml);
            return (true, "Success");
        }

        private async Task<FileInformation> GetMetadataAsync(string objectId)
        {
            FileInformation? metadata;
            try
            {
                metadata = await _metadataRepository.GetMetadataAsync(objectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting metadata", ex);
            }
            if (metadata == null) throw new CpsException("Error while getting metadata: metadata is null");
            return metadata;
        }

        private async Task<bool> CheckPublicationDateAsync(FileInformation metadata, string objectId)
        {
            if (metadata.AdditionalMetadata == null) throw new CpsException("Error while getting metadata: AdditionalMetadata is null");
            if (metadata.AdditionalMetadata.PublicationDate.HasValue && metadata.AdditionalMetadata.PublicationDate.Value.IsDateInFuture())
            {
                await _publicationRepository.AddToQueueAsync(objectId, metadata.AdditionalMetadata.PublicationDate.Value);
                return false;
            }
            return true;
        }

        private async Task<Stream> GetStreamAsync(ObjectIdentifiers objectIdentifiers)
        {
            Stream? stream;
            try
            {
                stream = await _driveRepository.GetStreamAsync(objectIdentifiers.DriveId, objectIdentifiers.DriveItemId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting content", ex);
            }
            if (stream == null) throw new CpsException("Error while getting content: stream is null");
            return stream;
        }

        private async Task CreateContentAsync(ObjectIdentifiers objectIdentifiers, FileInformation metadata, Stream stream)
        {
            if (metadata.MimeType == null) throw new CpsException($"No {nameof(FileInformation.MimeType)} found for {nameof(metadata)}");
            try
            {
                var fileName = objectIdentifiers.ObjectId + "." + metadata.FileExtension;
                await _fileStorageService.CreateAsync(_globalSettings.ContentContainerName, fileName, stream, metadata.MimeType, objectIdentifiers.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while uploading document", ex);
            }
        }

        private string GetMetadataAsXml(FileInformation metadata)
        {
            try
            {
                return _xmlExportSerivce.GetMetadataAsXml(metadata);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while exporting metadata to xml", ex);
            }
        }

        private async Task CreateMetadataXmlAsync(ObjectIdentifiers objectIdentifiers, string metadataXml)
        {
            try
            {
                var metadataName = objectIdentifiers.ObjectId + ".xml";
                await _fileStorageService.CreateAsync(_globalSettings.MetadataContainerName, metadataName, metadataXml, "application/xml", objectIdentifiers.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while uploading metadata", ex);
            }
        }

        private async Task DeleteIfExistsFileAndXmlFromFileStorageAsync(ObjectIdentifiers objectIdentifiers)
        {
            try
            {
                await _fileStorageService.DeleteAsync(_globalSettings.ContentContainerName, objectIdentifiers.ObjectId, deleteIfExists: true);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while deleting document from content", ex);
            }
            try
            {
                await _fileStorageService.DeleteAsync(_globalSettings.MetadataContainerName, objectIdentifiers.ObjectId, deleteIfExists: true);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while deleting document from metadata", ex);
            }
            try
            {
                await _publicationRepository.RemoveFromQueueIfExistsAsync(objectIdentifiers.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while deleting document from toBePublished", ex);
            }
        }

        #region Synchronisation

        private async Task<bool> SynchroniseDocumentsAsync(ObjectIdentifiers objectIdentifiers, SynchronisationType synchronisationType)
        {
            bool succeeded;
            var errorMessage = string.Empty;
            if (synchronisationType == SynchronisationType.delete)
            {
                await DeleteIfExistsFileAndXmlFromFileStorageAsync(objectIdentifiers);
                succeeded = true;
            }
            else
            {
                (succeeded, errorMessage) = await UploadFileAndXmlToFileStorageAsync(objectIdentifiers);
            }

            var traceSynchronisationPart = "Deleted";
            if (synchronisationType == SynchronisationType.create) traceSynchronisationPart = "New";
            if (synchronisationType == SynchronisationType.update) traceSynchronisationPart = "Updated";
            if (!succeeded)
            {
                _telemetryClient.TrackTrace($"{traceSynchronisationPart} document synchronisation failed", new Dictionary<string, string>
                {
                    { nameof(objectIdentifiers.ObjectId), objectIdentifiers.ObjectId },
                    { nameof(objectIdentifiers.DriveItemId), objectIdentifiers.DriveItemId },
                    { "ErrorMessage", errorMessage }
                });
                return false;
            }

            // Callback for changed file.
            await _callbackRepository.CallCallbackAsync(objectIdentifiers.ObjectId, synchronisationType);

            _telemetryClient.TrackTrace($"{traceSynchronisationPart} document synchronisation succeeded", new Dictionary<string, string>
            {
                { nameof(objectIdentifiers.ObjectId), objectIdentifiers.ObjectId },
                { nameof(objectIdentifiers.DriveItemId), objectIdentifiers.DriveItemId }
            });
            return true;
        }

        #endregion


        #region Helpers

        private async Task<ObjectIdentifiers> GetObjectIdentifiersAsync(string objectId)
        {
            var objectIdentifiers = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (objectIdentifiers == null)
            {
                throw new CpsException("objectIdentifiersEntity is null");
            }
            return objectIdentifiers;
        }

        private void TrackCpsException(Exception exception, string? siteId = null, string? listId = null, string? listItemId = null, string? objectId = null, string? errorMessage = null)
        {
            var properties = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(siteId))
            {
                properties.Add(nameof(siteId), siteId);
            }
            if (!string.IsNullOrEmpty(listId))
            {
                properties.Add(nameof(listId), listId);
            }
            if (!string.IsNullOrEmpty(listItemId))
            {
                properties.Add(nameof(listItemId), listItemId);
            }
            if (!string.IsNullOrEmpty(objectId))
            {
                properties.Add(nameof(objectId), objectId);
            }
            if (!string.IsNullOrEmpty(errorMessage))
            {
                properties.Add(nameof(errorMessage), errorMessage);
            }
            _logger.LogError(exception, Constants.ErrorMessagePropertiesFormatString, exception.Message, properties);
        }

        #endregion
    }
}