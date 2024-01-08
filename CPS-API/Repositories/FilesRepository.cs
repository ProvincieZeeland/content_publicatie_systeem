﻿using System.Net;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
using FileInformation = CPS_API.Models.FileInformation;

namespace CPS_API.Repositories
{
    public interface IFilesRepository
    {
        Task<CpsFile> GetFileAsync(string objectId);

        Task<string> GetUrlAsync(string objectId, bool getAsUser = false);

        Task<ObjectIdentifiers> CreateLargeFileAsync(string source, string classification, IFormFile formFile);

        Task<ObjectIdentifiers> CreateFileByBytesAsync(FileInformation metadata, byte[] content);

        Task<ObjectIdentifiers> CreateFileByStreamAsync(FileInformation metadata, Stream fileStream);

        Task UpdateContentAsync(string objectId, byte[] content, bool getAsUser = false);

        Task UpdateContentAsync(string objectId, IFormFile formFile, bool getAsUser = false);

        Task UpdateContentAsync(string objectId, Stream fileStream, bool getAsUser = false);
    }

    public class FilesRepository : IFilesRepository
    {
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IDriveRepository _driveRepository;
        private readonly IMetadataRepository _metadataRepository;
        private readonly TelemetryClient _telemetryClient;

        public FilesRepository(
            IObjectIdRepository objectIdRepository,
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            IDriveRepository driveRepository,
            TelemetryClient telemetryClient,
            IMetadataRepository metadataRepository)
        {
            _objectIdRepository = objectIdRepository;
            _globalSettings = settings.Value;
            _driveRepository = driveRepository;
            _telemetryClient = telemetryClient;
            _metadataRepository = metadataRepository;
        }

        public async Task<string> GetUrlAsync(string objectId, bool getAsUser = false)
        {
            var objectIdentifiers = await GetObjectIdentifiersAsync(objectId);
            var driveItem = await GetDriveItemAsync(objectId, objectIdentifiers, getAsUser);
            return driveItem.WebUrl;
        }

        private async Task<ObjectIdentifiersEntity> GetObjectIdentifiersAsync(string objectId)
        {
            ObjectIdentifiersEntity? objectIdentifiers = null;
            try
            {
                objectIdentifiers = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            }
            catch (Exception ex) when (ex.InnerException is not UnauthorizedAccessException && ex is not FileNotFoundException)
            {
                throw new CpsException("Error while getting objectIdentifiers", ex);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
            }
            if (objectIdentifiers == null) throw new FileNotFoundException($"ObjectIdentifiers (objectId = {objectId}) does not exist!");
            return objectIdentifiers;
        }

        private async Task<DriveItem> GetDriveItemAsync(string objectId, ObjectIdentifiersEntity objectIdentifiers, bool getAsUser = false)
        {
            DriveItem? driveItem = null;
            try
            {
                driveItem = await _driveRepository.GetDriveItemAsync(objectIdentifiers.SiteId, objectIdentifiers.ListId, objectIdentifiers.ListItemId, getAsUser);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw;
            }
            catch (Exception ex) when (ex is MsalUiRequiredException || ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                throw;
            }
            catch (Exception ex) when (ex.InnerException is not UnauthorizedAccessException && ex.Message != "Access denied")
            {
                throw new CpsException("Error while getting driveItem", ex);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
            }
            if (driveItem == null) throw new FileNotFoundException($"DriveItem (objectId = {objectId}) does not exist!");
            return driveItem;
        }

        public async Task<CpsFile> GetFileAsync(string objectId)
        {
            FileInformation metadata;
            try
            {
                metadata = await _metadataRepository.GetMetadataAsync(objectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting metadata", ex);
            }
            if (metadata == null) throw new FileNotFoundException($"Metadata (objectId = {objectId}) does not exist!");

            return new CpsFile
            {
                Metadata = metadata
            };
        }

        public async Task<ObjectIdentifiers> CreateLargeFileAsync(string source, string classification, IFormFile formFile)
        {
            var file = GetNewLargeFile(source, classification, formFile);
            if (formFile == null) throw new ArgumentNullException(nameof(formFile));
            return await CreateFileAsync(file.Metadata, formFile: formFile);
        }

        private CpsFile GetNewLargeFile(string source, string classification, IFormFile formFile)
        {
            var file = new CpsFile();
            file.Metadata = new FileInformation();
            file.Metadata.FileName = formFile.FileName;
            file.Metadata.AdditionalMetadata = new FileMetadata();
            file.Metadata.AdditionalMetadata.Source = source;
            file.Metadata.AdditionalMetadata.Classification = classification;

            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                if (fieldMapping.DefaultValue != null)
                {
                    var defaultAsStr = fieldMapping.DefaultValue?.ToString();
                    if (!defaultAsStr.IsNullOrEmpty())
                    {
                        if (MetadataHelper.FieldIsMainMetadata(fieldMapping.FieldName))
                        {
                            file.Metadata[fieldMapping.FieldName] = fieldMapping.DefaultValue;
                        }
                        else
                        {
                            file.Metadata.AdditionalMetadata[fieldMapping.FieldName] = fieldMapping.DefaultValue;
                        }
                    }
                }
            }
            return file;
        }

        public async Task<ObjectIdentifiers> CreateFileByBytesAsync(FileInformation metadata, byte[] content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            return await CreateFileAsync(metadata, content: content);
        }

        public async Task<ObjectIdentifiers> CreateFileByStreamAsync(FileInformation metadata, Stream fileStream)
        {
            if (fileStream == null) throw new ArgumentNullException(nameof(fileStream));
            return await CreateFileAsync(metadata, fileStream: fileStream);
        }

        private async Task<ObjectIdentifiers> CreateFileAsync(FileInformation metadata, byte[]? content = null, IFormFile? formFile = null, Stream? fileStream = null)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException(nameof(metadata.AdditionalMetadata));

            // Get driveid or site matching classification & source
            var locationMapping = GetLocationMapping(metadata);
            if (locationMapping == null) throw new CpsException($"{nameof(locationMapping)} does not exist ({nameof(metadata.AdditionalMetadata.Classification)}: \"{metadata.AdditionalMetadata.Classification}\", {nameof(metadata.AdditionalMetadata.Source)}: \"{metadata.AdditionalMetadata.Source}\")");

            var drive = await _driveRepository.GetDriveAsync(locationMapping.SiteId, locationMapping.ListId);
            if (drive == null) throw new CpsException("Drive not found for new file.");

            // Add new file to SharePoint
            DriveItem? driveItem;
            try
            {
                driveItem = await CreateFileAsync(drive, metadata, content, formFile, fileStream);
                if (driveItem == null)
                {
                    throw new CpsException("Error while adding new file");
                }
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Conflict && ex.Error.Code == "nameAlreadyExists")
            {
                throw new NameAlreadyExistsException($"The specified {nameof(metadata.FileName)} ({metadata.FileName}) already exists");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while adding new file", ex);
            }

            // Get new identifiers.
            metadata.Ids = await GetNewObjectIdentifiersAsync(drive, driveItem, locationMapping);

            // Handle the new file.
            return await handleCreatedFile(metadata, formFile != null);
        }

        /// <summary>
        /// Get driveid or site matching classification & source
        /// </summary>
        private LocationMapping? GetLocationMapping(FileInformation metadata)
        {
            return _globalSettings.LocationMapping.Find(item =>
                item.Classification.Equals(metadata.AdditionalMetadata.Classification, StringComparison.OrdinalIgnoreCase)
                && item.Source.Equals(metadata.AdditionalMetadata.Source, StringComparison.OrdinalIgnoreCase)
            );
        }

        private async Task<DriveItem> CreateFileAsync(Drive drive, FileInformation metadata, byte[]? content = null, IFormFile? formFile = null, Stream? fileStream = null)
        {
            if (formFile != null)
            {
                using var stream = formFile.OpenReadStream();
                return await CreateFileAsync(drive.Id, metadata.FileName, stream);
            }
            else if (content != null)
            {
                using var stream = new MemoryStream(content);
                if (stream.Length > 0)
                {
                    stream.Position = 0;
                }
                return await CreateFileAsync(drive.Id, metadata.FileName, stream);
            }
            else
            {
                return await CreateFileAsync(drive.Id, metadata.FileName, fileStream);
            }
        }

        private async Task<ObjectIdentifiers> GetNewObjectIdentifiersAsync(Drive drive, DriveItem driveItem, LocationMapping locationMapping)
        {
            var ids = new ObjectIdentifiers();
            ids.DriveId = drive.Id;
            ids.DriveItemId = driveItem.Id;
            ids.ExternalReferenceListId = locationMapping.ExternalReferenceListId;
            return await _objectIdRepository.FindMissingIds(ids);
        }

        private async Task<DriveItem> CreateFileAsync(string driveId, string fileName, Stream fileStream)
        {
            if (fileStream.Length > 0)
            {
                return await _driveRepository.CreateAsync(driveId, fileName, fileStream);
            }
            else
            {
                throw new CpsException("File cannot be empty");
            }
        }

        public async Task<ObjectIdentifiers> handleCreatedFile(FileInformation metadata, bool ignoreRequiredFields = false)
        {
            // Generate objectId
            try
            {
                string objectId = await _objectIdRepository.GenerateObjectIdAsync(metadata.Ids);
                if (string.IsNullOrEmpty(objectId)) throw new CpsException("ObjectId is empty");

                metadata.Ids.ObjectId = objectId;
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(metadata.Ids.DriveId, metadata.Ids.DriveItemId);

                throw new CpsException("Error while generating ObjectId", ex);
            }

            // Update ObjectId and metadata in Sharepoint with Graph
            try
            {
                await _metadataRepository.UpdateMetadataWithoutExternalReferencesAsync(metadata, isForNewFile: true, ignoreRequiredFields);
            }
            catch (FieldRequiredException)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(metadata.Ids.DriveId, metadata.Ids.DriveItemId);

                throw;
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(metadata.Ids.DriveId, metadata.Ids.DriveItemId);

                throw new CpsException("Error while updating metadata", ex);
            }

            // Update ExternalReferences in Sharepoint with Graph
            try
            {
                await _metadataRepository.UpdateExternalReferencesAsync(metadata);
            }
            catch (FieldRequiredException)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(metadata.Ids.DriveId, metadata.Ids.DriveItemId);

                throw;
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(metadata.Ids.DriveId, metadata.Ids.DriveItemId);

                throw new CpsException("Error while updating external references", ex);
            }

            // Store any additional IDs passed along
            try
            {
                await _metadataRepository.UpdateAdditionalIdentifiers(metadata);
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(metadata.Ids.DriveId, metadata.Ids.DriveItemId);

                throw new CpsException("Error while updating additional IDs", ex);
            }

            // Done
            return metadata.Ids;
        }

        public async Task UpdateContentAsync(string objectId, byte[] content, bool getAsUser = false)
        {
            await GetIdentifiersAndUpdateContentAsync(objectId, content: content, getAsUser: getAsUser);
        }

        public async Task UpdateContentAsync(string objectId, IFormFile formFile, bool getAsUser = false)
        {
            await GetIdentifiersAndUpdateContentAsync(objectId, formFile: formFile, getAsUser: getAsUser);
        }

        public async Task UpdateContentAsync(string objectId, Stream fileStream, bool getAsUser = false)
        {
            await GetIdentifiersAndUpdateContentAsync(objectId, fileStream: fileStream, getAsUser: getAsUser);
        }

        private async Task GetIdentifiersAndUpdateContentAsync(string objectId, byte[]? content = null, IFormFile? formFile = null, Stream? fileStream = null, bool getAsUser = false)
        {
            // Get objectIdentifiers
            ObjectIdentifiersEntity? ids;
            try
            {
                ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting objectIdentifiers", ex);
            }
            if (ids == null) throw new FileNotFoundException("ObjectIdentifiers not found");

            // Create new version
            try
            {
                if (content != null && content.Length > 0)
                {
                    using var stream = new MemoryStream(content);
                    await UpdateContentAsync(ids.DriveId, ids.DriveItemId, stream, getAsUser);
                }
                else if (formFile != null)
                {
                    using var stream = formFile.OpenReadStream();
                    await UpdateContentAsync(ids.DriveId, ids.DriveItemId, stream, getAsUser);
                }
                else if (fileStream != null)
                {
                    await UpdateContentAsync(ids.DriveId, ids.DriveItemId, fileStream, getAsUser);
                }
                else
                {
                    throw new CpsException("File cannot be empty");
                }
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while updating driveItem", ex);
            }
        }

        private async Task<DriveItem?> UpdateContentAsync(string driveId, string driveItemId, Stream fileStream, bool getAsUser = false)
        {
            if (fileStream.Length > 0)
            {
                return await _driveRepository.UpdateContentAsync(driveId, driveItemId, fileStream, getAsUser);
            }
            else
            {
                throw new CpsException("File cannot be empty");
            }
        }
    }
}