using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using CPS_API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Controllers
{
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("[controller]")]
    [ApiController]
    public class ExportController : Controller
    {
        private readonly IDriveRepository _driveRepository;

        private readonly ISettingsRepository _settingsRepository;

        private readonly FileStorageService _fileStorageService;

        private readonly StorageTableService _storageTableService;

        private readonly IFilesRepository _filesRepository;

        private readonly GlobalSettings _globalSettings;

        private readonly XmlExportSerivce _xmlExportSerivce;

        private readonly ILogger _logger;

        public ExportController(IDriveRepository driveRepository,
                                ISettingsRepository settingsRepository,
                                FileStorageService fileStorageService,
                                StorageTableService storageTableService,
                                IFilesRepository filesRepository,
                                IOptions<GlobalSettings> settings,
                                XmlExportSerivce xmlExportSerivce,
                                ILogger<FilesRepository> logger)
        {
            _driveRepository = driveRepository;
            _settingsRepository = settingsRepository;
            _fileStorageService = fileStorageService;
            _storageTableService = storageTableService;
            _filesRepository = filesRepository;
            _globalSettings = settings.Value;
            _xmlExportSerivce = xmlExportSerivce;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // GET
        [HttpGet]
        [Route("new")]
        public async Task<IActionResult> SynchroniseNewDocuments()
        {
            // Get last synchronisation date.
            DateTime? startDate;
            try
            {
                startDate = await _settingsRepository.GetLastSynchronisationNewAsync();
                startDate = new DateTime(2023, 3, 14);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while getting LastSynchronisation");
            }
            if (startDate == null) startDate = DateTime.Now.Date;

            // Get all new files from known locations
            List<DeltaDriveItem> newItems;
            try
            {
                newItems = await _driveRepository.GetNewItems(startDate.Value);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while getting new documents");
            }
            if (newItems == null) return StatusCode(500, "Error while getting new documents");

            // For each file:
            // generate xml from metadata
            // upload file to storage container
            // upload xml to storage container
            var itemsSuccesfulAdded = true;
            foreach (var newItem in newItems)
            {
                try
                {
                    ObjectIdentifiersEntity? objectIdentifiersEntity;
                    try
                    {
                        objectIdentifiersEntity = await GetObjectIdentifiersEntityAsync(newItem.DriveId, newItem.Id);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Error while getting objectIdentifiers");
                    }
                    if (objectIdentifiersEntity == null) throw new Exception("Error while getting objectIdentifiers");
                    var succeeded = await UploadFileAndXmlToFileStorage(objectIdentifiersEntity, newItem.Name);

                    // Callback for changed file.
                    if (succeeded && !_globalSettings.CallbackUrl.IsNullOrEmpty())
                    {
                        var fileInfo = await _filesRepository.GetFileAsync(objectIdentifiersEntity.ObjectId);
                        var callbackFileInfo = new CallbackCpsFile(fileInfo);
                        var body = JsonSerializer.Serialize(callbackFileInfo);
                        var callbackUrl = _globalSettings.CallbackUrl + $"/create/{objectIdentifiersEntity.ObjectId}";

                        await CallCallbackUrl(callbackUrl, body);
                    }
                }
                catch (Exception ex)
                {
                    itemsSuccesfulAdded = false;
                    _logger.LogError($"Error while adding file (DriveId: {newItem?.DriveId}, DriveItemId: {newItem?.Id}) to FileStorage: {ex.Message}");
                }
            }

            return itemsSuccesfulAdded ? Ok() : StatusCode(500, "Not all new items are added");
        }

        [HttpGet]
        [Route("updated")]
        public async Task<IActionResult> SynchroniseUpdatedDocuments()
        {
            // Get last synchronisation date.
            DateTime? startDate;
            try
            {
                startDate = await _settingsRepository.GetLastSynchronisationChangedAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error while getting LastSynchronisation");
            }
            if (startDate == null) startDate = DateTime.Now.Date;

            // Get all updated files from known locations
            List<DeltaDriveItem> updatedItems;
            try
            {
                updatedItems = await _driveRepository.GetUpdatedItems(startDate.Value);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error while getting updated documents");
            }
            if (updatedItems == null) return StatusCode(500, "Error while getting updated documents");

            // For each file:
            // generate xml from metadata
            // upload file to storage container
            // upload xml to storage container
            var itemsSuccesfulUpdated = true;
            foreach (var updatedItem in updatedItems)
            {
                try
                {
                    ObjectIdentifiersEntity? objectIdentifiersEntity;
                    try
                    {
                        objectIdentifiersEntity = await GetObjectIdentifiersEntityAsync(updatedItem.DriveId, updatedItem.Id);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Error while getting objectIdentifiers");
                    }
                    if (objectIdentifiersEntity == null) throw new Exception("Error while getting objectIdentifiers");
                    var succeeded = await UploadFileAndXmlToFileStorage(objectIdentifiersEntity, updatedItem.Name);

                    // Callback for changed file.
                    if (succeeded && !_globalSettings.CallbackUrl.IsNullOrEmpty())
                    {
                        var fileInfo = await _filesRepository.GetFileAsync(objectIdentifiersEntity.ObjectId);
                        var callbackFileInfo = new CallbackCpsFile(fileInfo);
                        var body = JsonSerializer.Serialize(callbackFileInfo);
                        var callbackUrl = _globalSettings.CallbackUrl + $"/update/{objectIdentifiersEntity.ObjectId}";

                        await CallCallbackUrl(callbackUrl, body);
                    }
                }
                catch (Exception ex)
                {
                    itemsSuccesfulUpdated = false;
                    _logger.LogError($"Error while updating file (DriveId: {updatedItem?.DriveId}, DriveItemId: {updatedItem?.Id}) to FileStorage: {ex.Message}");
                }
            }

            return itemsSuccesfulUpdated ? Ok() : StatusCode(500, "Not all new items are updated");
        }

        private async Task<bool> UploadFileAndXmlToFileStorage(ObjectIdentifiersEntity objectIdentifiersEntity, string name)
        {
            bool metadataExists;
            try
            {
                var ids = new ObjectIdentifiers(objectIdentifiersEntity);
                metadataExists = await _filesRepository.FileContainsMetadata(ids);
            }
            catch
            {
                throw new Exception("Error while getting metadata");
            }
            // When metadata is unknown, we skip the synchronisation.
            // The file is a new incomplete file or something went wrong while adding the file.
            if (!metadataExists)
            {
                return false;
            }

            FileInformation? metadata;
            try
            {
                metadata = await _filesRepository.GetMetadataAsync(objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while getting metadata");
            }
            if (metadata == null) throw new Exception("Error while getting metadata");

            Stream? stream;
            try
            {
                stream = await _driveRepository.DownloadAsync(objectIdentifiersEntity.DriveId, objectIdentifiersEntity.DriveItemId);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while getting content");
            }
            if (stream == null) throw new Exception("Error while getting content");

            var fileName = objectIdentifiersEntity.ObjectId + "." + metadata.FileExtension;
            try
            {
                await _fileStorageService.CreateAsync(_globalSettings.ContentContainerName, fileName, stream, metadata.MimeType, objectIdentifiersEntity.ObjectId);
                //todo: get full filelocation for sending to callback?
            }
            catch (Exception ex)
            {
                throw new Exception("Error while uploading document");
            }

            string metadataXml;
            try
            {
                metadataXml = _xmlExportSerivce.GetMetadataAsXml(metadata);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while exporting metadata to xml");
            }

            var metadataName = objectIdentifiersEntity.ObjectId + ".xml";
            try
            {
                await _fileStorageService.CreateAsync(_globalSettings.MetadataContainerName, metadataName, metadataXml, "application/xml", objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while uploading metadata");
            }

            return true;
        }

        [HttpGet]
        [Route("deleted")]
        public async Task<IActionResult> SynchroniseDeletedDocuments()
        {
            // Get last synchronisation date.
            DateTime? startDate;
            try
            {
                startDate = await _settingsRepository.GetLastSynchronisationDeletedAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error while getting LastSynchronisation");
            }
            if (startDate == null) startDate = DateTime.Now.Date;

            // Get all deleted files from known locations
            List<DeltaDriveItem> deletedItems;
            try
            {
                deletedItems = await _driveRepository.GetDeletedItems(startDate.Value);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error while getting deleted documents");
            }
            if (deletedItems == null) return StatusCode(500, "Error while getting deleted documents");

            // For each file:
            // delete file from storage container
            // delete xml from storage container
            var itemsSuccesfulDeleted = true;
            foreach (var deletedItem in deletedItems)
            {
                try
                {
                    ObjectIdentifiersEntity? objectIdentifiersEntity;
                    try
                    {
                        objectIdentifiersEntity = await GetObjectIdentifiersEntityAsync(deletedItem.DriveId, deletedItem.Id);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Error while getting objectIdentifiers");
                    }
                    if (objectIdentifiersEntity == null) throw new Exception("Error while getting objectIdentifiers");
                    await DeleteFileAndXmlFromFileStorage(objectIdentifiersEntity);

                    // Callback for changed file.
                    if (!_globalSettings.CallbackUrl.IsNullOrEmpty())
                    {
                        var callbackUrl = _globalSettings.CallbackUrl + $"/delete/{objectIdentifiersEntity.ObjectId}";
                        await CallCallbackUrl(callbackUrl);
                    }
                }
                catch (Exception ex)
                {
                    itemsSuccesfulDeleted = false;
                    _logger.LogError($"Error while deleting file (DriveId: {deletedItem?.DriveId}, DriveItemId: {deletedItem?.Id}) to FileStorage: {ex.Message}");
                }
            }

            return itemsSuccesfulDeleted ? Ok() : StatusCode(500, "Not all new items are deleted");
        }

        private async Task CallCallbackUrl(string url, string body = "")
        {
            using (var client = new HttpClient())
            {
                try
                {
                    var method = body.IsNullOrEmpty() ? HttpMethod.Get : HttpMethod.Post;
                    var request = new HttpRequestMessage(method, url);
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _globalSettings.CallbackAccessToken);
                    if (!body.IsNullOrEmpty())
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    }

                    var response = await client.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"Error while sending sync callback: " + response.ToString());
                    }
                }
                catch (Exception ex)
                {
                    // Log error to callback service, otherwise ignore it
                    _logger.LogError($"Error while sending sync callback: " + ex.Message);
                }
            }
        }

        private async Task DeleteFileAndXmlFromFileStorage(ObjectIdentifiersEntity objectIdentifiersEntity)
        {
            try
            {
                await _fileStorageService.DeleteAsync(_globalSettings.ContentContainerName, objectIdentifiersEntity.ObjectId);
                await _fileStorageService.DeleteAsync(_globalSettings.MetadataContainerName, objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while deleting document");
            }
        }

        private CloudTable GetObjectIdentifiersTable()
        {
            var table = _storageTableService.GetTable(_globalSettings.ObjectIdentifiersTableName);
            if (table == null)
            {
                throw new Exception($"Tabel \"{_globalSettings.ObjectIdentifiersTableName}\" not found");
            }
            return table;
        }

        private async Task<ObjectIdentifiersEntity?> GetObjectIdentifiersEntityAsync(string driveId, string driveItemId)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();

            var filterDrive = TableQuery.GenerateFilterCondition("DriveId", QueryComparisons.Equal, driveId);
            var filter = TableQuery.GenerateFilterCondition("DriveItemId", QueryComparisons.Equal, driveItemId);
            var query = new TableQuery<ObjectIdentifiersEntity>().Where(filterDrive).Where(filter);

            var result = await objectIdentifiersTable.ExecuteQuerySegmentedAsync(query, null);
            return result.Results?.FirstOrDefault();
        }
    }
}