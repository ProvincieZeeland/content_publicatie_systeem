using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Options;

namespace CPS_API.Repositories
{
    public interface IExportRepository
    {
        Task<ExportResponse> SynchroniseNewDocumentsAsync(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens);

        Task<ExportResponse> SynchroniseUpdatedDocumentsAsync(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens);

        Task<ExportResponse> SynchroniseDeletedDocumentsAsync(Dictionary<string, string> tokens);

        Task<ToBePublishedExportResponse> SynchroniseToBePublishedDocumentsAsync();
    }

    public class ExportRepository : IExportRepository
    {
        private readonly IDriveRepository _driveRepository;
        private readonly ISettingsRepository _settingsRepository;
        private readonly IMetadataRepository _sharePointRepository;
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly IPublicationRepository _publicationRepository;
        private readonly ICallbackRepository _callbackRepository;

        private readonly FileStorageService _fileStorageService;
        private readonly XmlExportSerivce _xmlExportSerivce;

        private readonly GlobalSettings _globalSettings;

        private readonly TelemetryClient _telemetryClient;

        public ExportRepository(
            IDriveRepository driveRepository,
            ISettingsRepository settingsRepository,
            IMetadataRepository sharePointRepository,
            IObjectIdRepository objectIdRepository,
            IPublicationRepository publicationRepository,
            ICallbackRepository callbackRepository,
            FileStorageService fileStorageService,
            XmlExportSerivce xmlExportSerivce,
            IOptions<GlobalSettings> settings,
            TelemetryClient telemetryClient)
        {
            _driveRepository = driveRepository;
            _settingsRepository = settingsRepository;
            _sharePointRepository = sharePointRepository;
            _objectIdRepository = objectIdRepository;
            _publicationRepository = publicationRepository;
            _callbackRepository = callbackRepository;
            _fileStorageService = fileStorageService;
            _xmlExportSerivce = xmlExportSerivce;
            _globalSettings = settings.Value;
            _telemetryClient = telemetryClient;
        }

        #region New Documents

        public async Task<ExportResponse> SynchroniseNewDocumentsAsync(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens)
        {
            // Get all new files from known locations
            DeltaResponse deltaResponse;
            try
            {
                deltaResponse = await GetNewItemsAsync(lastSynchronisation, tokens);
            }
            catch (Exception ex)
            {
                TrackCpsException(ex);
                await StopNewSynchronisationAsync();
                throw new CpsException("Error while getting new documents: " + ex.Message);
            }

            // Synchronise each file
            return await SynchroniseNewDocumentsAsync(deltaResponse);
        }

        private async Task<DeltaResponse> GetNewItemsAsync(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens)
        {
            var deltaResponse = await _driveRepository.GetNewItems(lastSynchronisation, tokens);
            if (deltaResponse == null)
            {
                throw new CpsException("Deltaresponse is null");
            }
            return deltaResponse;
        }

        private async Task StopNewSynchronisationAsync()
        {
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsNewSynchronisationRunningField, false);
            _telemetryClient.TrackEvent("New item synchronisation has stopped");
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
            var entities = await _publicationRepository.GetEntitiesAsync();
            entities = entities.Where(entity => entity.PublicationDate.Date <= DateTimeOffset.UtcNow.Date).ToList();

            var itemsAdded = 0;
            var failedToBePublishedEntities = new List<ToBePublishedEntity>();
            foreach (var entity in entities)
            {
                ObjectIdentifiersEntity? objectIdentifiersEntity;
                try
                {
                    objectIdentifiersEntity = await GetObjectIdentifiersAsync(entity.ObjectId);
                }
                catch (Exception ex)
                {
                    failedToBePublishedEntities.Add(entity);
                    TrackCpsException(ex, objectId: entity.ObjectId, errorMessage: "Error while getting objectIdentifiers: " + ex.Message);
                    _telemetryClient.TrackTrace($"New document synchronisation failed (objectId = {entity})");
                    continue;
                }

                try
                {
                    var succeeded = await SynchroniseDocumentsAsync(objectIdentifiersEntity, "create");
                    if (succeeded)
                    {
                        await _publicationRepository.DeleteEntityAsync(entity);
                        itemsAdded++;
                    }
                }
                catch (Exception ex)
                {
                    failedToBePublishedEntities.Add(entity);
                    TrackCpsException(ex, objectIdentifiersEntity.DriveId, objectIdentifiersEntity.DriveItemId, objectIdentifiersEntity.ObjectId, "Error while synchronising new documents");
                    _telemetryClient.TrackTrace($"New document synchronisation failed (objectId = {objectIdentifiersEntity.ObjectId}, driveItemId = {objectIdentifiersEntity.DriveItemId})");
                }
            }

            return new ToBePublishedExportResponse(itemsAdded, failedToBePublishedEntities);
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
        private async Task<ExportResponse> SynchroniseNewDocumentsAsync(DeltaResponse deltaResponse)
        {
            var itemsAdded = 0;
            var notAddedItems = new List<DeltaDriveItem>();
            foreach (var newItem in deltaResponse.Items)
            {
                if (newItem == null)
                {
                    continue;
                }

                ObjectIdentifiersEntity? objectIdentifiersEntity;
                try
                {
                    objectIdentifiersEntity = await GetObjectIdentifiersAsync(newItem);
                }
                catch (Exception ex)
                {
                    notAddedItems.Add(newItem);
                    TrackCpsException(ex, newItem.DriveId, newItem.Id, errorMessage: "Error while getting objectIdentifiers: " + ex.Message);
                    _telemetryClient.TrackTrace($"New document synchronisation failed (driveId = {newItem.DriveId}, driveItemId = {newItem.Id}, name = {newItem.Name})");
                    continue;
                }

                try
                {
                    var succeeded = await SynchroniseDocumentsAsync(objectIdentifiersEntity, "create");
                    if (succeeded)
                    {
                        itemsAdded++;
                    }
                }
                catch (Exception ex)
                {
                    notAddedItems.Add(newItem);
                    TrackCpsException(ex, objectIdentifiersEntity.DriveId, objectIdentifiersEntity.DriveItemId, objectIdentifiersEntity.ObjectId, "Error while synchronising new documents");
                    _telemetryClient.TrackTrace($"New document synchronisation failed (objectId = {objectIdentifiersEntity.ObjectId}, driveItemId = {objectIdentifiersEntity.DriveItemId})");
                }
            }

            var newNextTokens = GetNewNextToken(deltaResponse);
            return new ExportResponse(newNextTokens, notAddedItems, itemsAdded);
        }

        #endregion

        #region Updated Documents

        public async Task<ExportResponse> SynchroniseUpdatedDocumentsAsync(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens)
        {
            // Get all updated files from known locations
            DeltaResponse deltaResponse;
            try
            {
                deltaResponse = await GetUpdatedItemsAsync(lastSynchronisation, tokens);
            }
            catch (Exception ex)
            {
                TrackCpsException(ex);
                await StopChangedSynchronisationAsync();
                throw new CpsException("Error while getting updated documents: " + ex.Message);
            }

            // Synchronise each file
            return await GetAndSynchroniseUpdatedDocumentsAsync(deltaResponse);
        }

        private async Task<DeltaResponse> GetUpdatedItemsAsync(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens)
        {
            var deltaResponse = await _driveRepository.GetUpdatedItems(lastSynchronisation, tokens);
            if (deltaResponse == null)
            {
                throw new CpsException("Deltaresponse is null");
            }
            return deltaResponse;
        }

        private async Task StopChangedSynchronisationAsync()
        {
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsChangedSynchronisationRunningField, false);
            _telemetryClient.TrackEvent("Changed item synchronisation has stopped");
        }

        /// <summary>
        /// For each file:
        ///  - Generate xml from metadata
        ///  - Upload file to storage container
        ///  - Upload xml to storage container
        ///  - Post new file to callback
        /// </summary>
        private async Task<ExportResponse> GetAndSynchroniseUpdatedDocumentsAsync(DeltaResponse deltaResponse)
        {
            var itemsUpdated = 0;
            var notUpdatedItems = new List<DeltaDriveItem>();
            foreach (var updatedItem in deltaResponse.Items)
            {
                if (updatedItem == null)
                {
                    continue;
                }

                ObjectIdentifiersEntity? objectIdentifiersEntity;
                try
                {
                    objectIdentifiersEntity = await GetObjectIdentifiersAsync(updatedItem);
                }
                catch (Exception ex)
                {
                    notUpdatedItems.Add(updatedItem);
                    TrackCpsException(ex, updatedItem.DriveId, updatedItem.Id, errorMessage: "Error while getting objectIdentifiers: " + ex.Message);
                    _telemetryClient.TrackTrace($"Updated document synchronisation failed (driveId = {updatedItem.DriveId}, driveItemId = {updatedItem.Id}, name = {updatedItem.Name})");
                    continue;
                }

                try
                {
                    var succeeded = await SynchroniseDocumentsAsync(objectIdentifiersEntity, "update");
                    if (succeeded)
                    {
                        itemsUpdated++;
                    }
                }
                catch (Exception ex)
                {
                    notUpdatedItems.Add(updatedItem);
                    TrackCpsException(ex, objectIdentifiersEntity.DriveId, objectIdentifiersEntity.DriveItemId, objectIdentifiersEntity.ObjectId, "Error while synchronising updated documents");
                    _telemetryClient.TrackEvent($"Error while updating file (DriveId: {objectIdentifiersEntity.DriveId}, DriveItemId: {objectIdentifiersEntity.DriveItemId}) in FileStorage: {ex.Message}");
                    _telemetryClient.TrackTrace($"Updated document synchronisation failed (objectId = {objectIdentifiersEntity.ObjectId}, driveItemId = {objectIdentifiersEntity.DriveItemId})");
                }
            }

            var newNextTokens = GetNewNextToken(deltaResponse);
            return new ExportResponse(newNextTokens, notUpdatedItems, itemsUpdated);
        }

        private async Task<bool> UploadFileAndXmlToFileStorageAsync(ObjectIdentifiersEntity objectIdentifiersEntity)
        {
            // When metadata is unknown, we skip the synchronisation.
            // The file is a new incomplete file or something went wrong while adding the file.
            var metadataExists = await FileContainsMetadataAsync(objectIdentifiersEntity);
            if (!metadataExists)
            {
                return false;
            }

            var metadata = await GetMetadataAsync(objectIdentifiersEntity.ObjectId);

            // Skip document when it is not ready for publishing.
            var isReadyForPublishing = await CheckPublicationDateAsync(metadata, objectIdentifiersEntity.ObjectId);
            if (!isReadyForPublishing)
            {
                return false;
            }

            var stream = await GetStreamAsync(objectIdentifiersEntity);
            await CreateContentAsync(objectIdentifiersEntity, metadata, stream);

            var metadataXml = GetMetadataAsXml(metadata);
            await CreateMetadataXmlAsync(objectIdentifiersEntity, metadataXml);
            return true;
        }

        private async Task<bool> FileContainsMetadataAsync(ObjectIdentifiersEntity objectIdentifiersEntity)
        {
            try
            {
                var ids = new ObjectIdentifiers(objectIdentifiersEntity);
                return await _sharePointRepository.FileContainsMetadata(ids);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting metadata", ex);
            }
        }

        private async Task<FileInformation> GetMetadataAsync(string objectId)
        {
            FileInformation? metadata;
            try
            {
                metadata = await _sharePointRepository.GetMetadataAsync(objectId);
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
            if (metadata.AdditionalMetadata.PublicationDate.HasValue && metadata.AdditionalMetadata.PublicationDate.Value.Date > DateTimeOffset.UtcNow.Date)
            {
                await _publicationRepository.SaveEntityAsync(objectId, metadata.AdditionalMetadata.PublicationDate.Value);
                return false;
            }
            return true;
        }

        private async Task<Stream> GetStreamAsync(ObjectIdentifiersEntity objectIdentifiersEntity)
        {
            Stream? stream;
            try
            {
                stream = await _driveRepository.GetStreamAsync(objectIdentifiersEntity.DriveId, objectIdentifiersEntity.DriveItemId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting content", ex);
            }
            if (stream == null) throw new CpsException("Error while getting content: stream is null");
            return stream;
        }

        private async Task CreateContentAsync(ObjectIdentifiersEntity objectIdentifiersEntity, FileInformation metadata, Stream stream)
        {
            try
            {
                var fileName = objectIdentifiersEntity.ObjectId + "." + metadata.FileExtension;
                await _fileStorageService.CreateAsync(_globalSettings.ContentContainerName, fileName, stream, metadata.MimeType, objectIdentifiersEntity.ObjectId);
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

        private async Task CreateMetadataXmlAsync(ObjectIdentifiersEntity objectIdentifiersEntity, string metadataXml)
        {
            try
            {
                var metadataName = objectIdentifiersEntity.ObjectId + ".xml";
                await _fileStorageService.CreateAsync(_globalSettings.MetadataContainerName, metadataName, metadataXml, "application/xml", objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while uploading metadata", ex);
            }
        }

        #endregion

        #region Deleted Documents

        public async Task<ExportResponse> SynchroniseDeletedDocumentsAsync(Dictionary<string, string> tokens)
        {
            // Get all deleted files from known locations
            DeltaResponse deltaResponse;
            try
            {
                deltaResponse = await GetDeletedItemsAsync(tokens);
            }
            catch (Exception ex)
            {
                TrackCpsException(ex);
                await StopDeletedSynchronisationAsync();
                throw new CpsException("Error while getting deleted documents: " + ex.Message);
            }

            // Synchronise each file
            return await GetAndSynchroniseDeletedDocumentsAsync(deltaResponse);
        }

        private async Task<DeltaResponse> GetDeletedItemsAsync(Dictionary<string, string> tokens)
        {
            var deltaResponse = await _driveRepository.GetDeletedItems(tokens);
            if (deltaResponse == null)
            {
                throw new CpsException("Deltaresponse is null");
            }
            return deltaResponse;
        }

        private async Task StopDeletedSynchronisationAsync()
        {
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsDeletedSynchronisationRunningField, false);
            _telemetryClient.TrackEvent("Deleted item synchronisation has stopped");
        }

        /// <summary>
        /// For each file:
        ///  - Generate xml from metadata
        ///  - Upload file to storage container
        ///  - Upload xml to storage container
        ///  - Post new file to callback
        /// </summary>
        private async Task<ExportResponse> GetAndSynchroniseDeletedDocumentsAsync(DeltaResponse deltaResponse)
        {
            var itemsDeleted = 0;
            var notDeletedItems = new List<DeltaDriveItem>();
            foreach (var deletedItem in deltaResponse.Items)
            {
                if (deletedItem == null)
                {
                    continue;
                }

                ObjectIdentifiersEntity? objectIdentifiersEntity;
                try
                {
                    objectIdentifiersEntity = await GetObjectIdentifiersAsync(deletedItem);
                }
                catch (Exception ex)
                {
                    notDeletedItems.Add(deletedItem);
                    TrackCpsException(ex, deletedItem.DriveId, deletedItem.Id, errorMessage: "Error while getting objectIdentifiers: " + ex.Message);
                    _telemetryClient.TrackTrace($"Deleted document synchronisation failed (driveId = {deletedItem.DriveId}, driveItemId = {deletedItem.Id}, name = {deletedItem.Name})");
                    continue;
                }

                try
                {
                    var succeeded = await SynchroniseDocumentsAsync(objectIdentifiersEntity, "delete");
                    if (succeeded)
                    {
                        itemsDeleted++;
                    }
                }
                catch (Exception ex)
                {
                    notDeletedItems.Add(deletedItem);
                    TrackCpsException(ex, objectIdentifiersEntity.DriveId, objectIdentifiersEntity.DriveItemId, objectIdentifiersEntity.ObjectId, "Error while synchronising deleted documents");
                    _telemetryClient.TrackEvent($"Error while deleting file (DriveId: {objectIdentifiersEntity.DriveId}, DriveItemId: {objectIdentifiersEntity.DriveItemId}) from FileStorage: {ex.Message}");
                    _telemetryClient.TrackTrace($"Deleted document synchronisation failed (objectId = {objectIdentifiersEntity.ObjectId}, driveItemId = {objectIdentifiersEntity.DriveItemId})");
                }
            }

            var newNextTokens = GetNewNextToken(deltaResponse);
            return new ExportResponse(newNextTokens, notDeletedItems, itemsDeleted);
        }

        private async Task DeleteIfExistsFileAndXmlFromFileStorageAsync(ObjectIdentifiersEntity objectIdentifiersEntity)
        {
            try
            {
                await _fileStorageService.DeleteIfExistsAsync(_globalSettings.ContentContainerName, objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while deleting document from content", ex);
            }
            try
            {
                await _fileStorageService.DeleteIfExistsAsync(_globalSettings.MetadataContainerName, objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while deleting document from metadata", ex);
            }
            try
            {
                await _publicationRepository.DeleteIfExistsEntityAsync(objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while deleting document from toBePublished", ex);
            }
        }

        #endregion

        #region Synchronisation

        private async Task<bool> SynchroniseDocumentsAsync(ObjectIdentifiersEntity objectIdentifiersEntity, string synchronisationType)
        {
            bool succeeded;
            if (synchronisationType == "delete")
            {
                await DeleteIfExistsFileAndXmlFromFileStorageAsync(objectIdentifiersEntity);
                succeeded = true;
            }
            else
            {
                succeeded = await UploadFileAndXmlToFileStorageAsync(objectIdentifiersEntity);
            }

            var traceSynchronisationPart = synchronisationType == "create" ? "New" : synchronisationType == "update" ? "Updated" : "Deleted";
            if (!succeeded)
            {
                _telemetryClient.TrackTrace($"{traceSynchronisationPart} document synchronisation failed (objectId = {objectIdentifiersEntity.ObjectId}, driveItemId = {objectIdentifiersEntity.DriveItemId})");
                return false;
            }

            // Callback for changed file.
            await _callbackRepository.CallCallbackAsync(objectIdentifiersEntity.ObjectId, synchronisationType);

            _telemetryClient.TrackTrace($"{traceSynchronisationPart} document synchronisation succeeded (objectId = {objectIdentifiersEntity.ObjectId}, driveItemId = {objectIdentifiersEntity.DriveItemId})");
            return true;
        }

        #endregion

        #region Helpers

        private async Task<ObjectIdentifiersEntity> GetObjectIdentifiersAsync(DeltaDriveItem driveItem)
        {
            var objectIdentifiersEntity = await _objectIdRepository.GetObjectIdentifiersAsync(driveItem.DriveId, driveItem.Id);
            if (objectIdentifiersEntity == null)
            {
                throw new CpsException("objectIdentifiersEntity is null");
            }
            return objectIdentifiersEntity;
        }

        private async Task<ObjectIdentifiersEntity> GetObjectIdentifiersAsync(string objectId)
        {
            var objectIdentifiersEntity = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (objectIdentifiersEntity == null)
            {
                throw new CpsException("objectIdentifiersEntity is null");
            }
            return objectIdentifiersEntity;
        }

        /// <summary>
        /// Dictionary to string for storage container.
        /// </summary>
        private static string GetNewNextToken(DeltaResponse deltaResponse)
        {
            return string.Join(";", deltaResponse.NextTokens.Select(x => x.Key + "=" + x.Value).ToArray());
        }

        private void TrackCpsException(Exception exception, string? driveId = null, string? driveItemId = null, string? objectId = null, string? errorMessage = null)
        {
            var properties = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(driveId))
            {
                properties.Add(nameof(driveId), driveId);
            }
            if (!string.IsNullOrEmpty(driveItemId))
            {
                properties.Add(nameof(driveItemId), driveItemId);
            }
            if (!string.IsNullOrEmpty(objectId))
            {
                properties.Add(nameof(objectId), objectId);
            }
            if (!string.IsNullOrEmpty(errorMessage))
            {
                properties.Add(nameof(errorMessage), errorMessage);
            }
            _telemetryClient.TrackException(exception, properties);
        }

        #endregion
    }
}