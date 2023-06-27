using System.IO;
using System.Net;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using FileInformation = CPS_API.Models.FileInformation;

namespace CPS_API.Repositories
{
    public interface IFilesRepository
    {
        Task<CpsFile> GetFileAsync(string objectId);

        Task<string> GetUrlAsync(string objectId, bool getAsUser = false);

        Task<ObjectIdentifiers> CreateFileAsync(CpsFile file, IFormFile? formFile = null);
        Task UpdateContentAsync(string objectId, IFormFile formFile, bool getAsUser = false);

        Task UpdateContentAsync(string objectId, byte[] content, bool getAsUser = false, IFormFile? formFile = null);
    }

    public class FilesRepository : IFilesRepository
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IDriveRepository _driveRepository;
        private readonly IMetadataRepository _sharePointRepository;
        private readonly TelemetryClient _telemetryClient;

        public FilesRepository(
            GraphServiceClient graphClient,
            IObjectIdRepository objectIdRepository,
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            IDriveRepository driveRepository,
            TelemetryClient telemetryClient,
            IMetadataRepository sharePointRepository)
        {
            _graphClient = graphClient;
            _objectIdRepository = objectIdRepository;
            _globalSettings = settings.Value;
            _driveRepository = driveRepository;
            _telemetryClient = telemetryClient;
            _sharePointRepository = sharePointRepository;
        }

        public async Task<string> GetUrlAsync(string objectId, bool getAsUser = false)
        {
            ObjectIdentifiersEntity objectIdentifiers = null;
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

            DriveItem driveItem = null;
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

            // Get url
            return driveItem.WebUrl;
        }

        public async Task<CpsFile> GetFileAsync(string objectId)
        {
            FileInformation metadata;
            try
            {
                metadata = await _sharePointRepository.GetMetadataAsync(objectId);
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

        public async Task<ObjectIdentifiers> CreateFileAsync(CpsFile file, IFormFile? formFile = null)
        {
            if (file == null || file.Metadata == null) throw new ArgumentNullException(nameof(file.Metadata));
            if (file.Metadata.AdditionalMetadata == null) throw new ArgumentNullException(nameof(file.Metadata.AdditionalMetadata));

            // Get driveid or site matching classification & source           
            var locationMapping = _globalSettings.LocationMapping.FirstOrDefault(item =>
                                    item.Classification.Equals(file.Metadata.AdditionalMetadata.Classification, StringComparison.OrdinalIgnoreCase)
                                    && item.Source.Equals(file.Metadata.AdditionalMetadata.Source, StringComparison.OrdinalIgnoreCase)
                                  );
            if (locationMapping == null) throw new CpsException($"{nameof(locationMapping)} does not exist ({nameof(file.Metadata.AdditionalMetadata.Classification)}: \"{file.Metadata.AdditionalMetadata.Classification}\", {nameof(file.Metadata.AdditionalMetadata.Source)}: \"{file.Metadata.AdditionalMetadata.Source}\")");


            var ids = new ObjectIdentifiers();
            ids.DriveId = locationMapping.ExternalReferenceListId;

            var drive = await _driveRepository.GetDriveAsync(locationMapping.SiteId, locationMapping.ListId);
            if (drive == null) throw new CpsException("Drive not found for new file.");
            ids.DriveId = drive.Id;

            // Add new file to SharePoint
            DriveItem? driveItem;
            try
            {
                if (formFile != null)
                {
                    using (var fileStream = formFile.OpenReadStream())
                    {
                        if (fileStream.Length > 0)
                        {
                            driveItem = await _driveRepository.CreateAsync(ids.DriveId, file.Metadata.FileName, fileStream);
                        }
                        else
                        {
                            throw new CpsException("File cannot be empty");
                        }
                    }
                }
                else
                {
                    using (var memorstream = new MemoryStream(file.Content))
                    {
                        if (memorstream.Length > 0)
                        {
                            memorstream.Position = 0;
                            driveItem = await _driveRepository.CreateAsync(ids.DriveId, file.Metadata.FileName, memorstream);
                        }
                        else
                        {
                            throw new CpsException("File cannot be empty");
                        }
                    }
                }

                if (driveItem == null)
                {
                    throw new CpsException("Error while adding new file");
                }

                ids.DriveItemId = driveItem.Id;
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Conflict && ex.Error.Code == "nameAlreadyExists")
            {
                throw new NameAlreadyExistsException($"The specified fileName ({file.Metadata.FileName}) already exists");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while adding new file", ex);
            }

            // Generate objectId
            try
            {
                string objectId = await _objectIdRepository.GenerateObjectIdAsync(ids);
                if (string.IsNullOrEmpty(objectId)) throw new CpsException("ObjectId is empty");

                ids.ObjectId = objectId;
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw new CpsException("Error while generating ObjectId", ex);
            }

            file.Metadata.Ids = ids;
            // Update ObjectId and metadata in Sharepoint with Graph
            try
            {
                await _sharePointRepository.UpdateMetadataWithoutExternalReferencesAsync(file.Metadata, isForNewFile: true, ignoreRequiredFields: formFile != null);
            }
            catch (FieldRequiredException)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw;
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw new CpsException("Error while updating metadata", ex);
            }

            // Update ExternalReferences in Sharepoint with Graph
            try
            {
                await _sharePointRepository.UpdateExternalReferencesAsync(file.Metadata);
            }
            catch (FieldRequiredException)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw;
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw new CpsException("Error while updating external references", ex);
            }

            // Store any additional IDs passed along
            try
            {
                await _sharePointRepository.UpdateAdditionalIdentifiers(file.Metadata);
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw new CpsException("Error while updating additional IDs", ex);
            }

            // Done
            return ids;
        }


        public async Task UpdateContentAsync(string objectId, IFormFile formFile, bool getAsUser = false)
        {
            await UpdateContentAsync(objectId, null, getAsUser, formFile);
        }

        public async Task UpdateContentAsync(string objectId, byte[] content, bool getAsUser = false, IFormFile? formFile = null)
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
                    using (var stream = new MemoryStream(content))
                    {
                        if (stream.Length > 0)
                        {
                            await _driveRepository.UpdateAsync(ids.DriveId, ids.DriveItemId, stream);
                        }
                        else
                        {
                            throw new CpsException("File cannot be empty");
                        }
                    }
                }
                else if (formFile != null)
                {
                    using (var fileStream = formFile.OpenReadStream())
                    {
                        if (fileStream.Length > 0)
                        {
                            await _driveRepository.UpdateAsync(ids.DriveId, ids.DriveItemId, fileStream);
                        }
                        else
                        {
                            throw new CpsException("File cannot be empty");
                        }
                    }
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
    }
}