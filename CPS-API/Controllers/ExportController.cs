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
using Microsoft.Graph;
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

        public ExportController(IDriveRepository driveRepository,
                                ISettingsRepository settingsRepository,
                                FileStorageService fileStorageService,
                                StorageTableService storageTableService,
                                IFilesRepository filesRepository,
                                IOptions<GlobalSettings> settings,
                                XmlExportSerivce xmlExportSerivce)
        {
            _driveRepository = driveRepository;
            _settingsRepository = settingsRepository;
            _fileStorageService = fileStorageService;
            _storageTableService = storageTableService;
            _filesRepository = filesRepository;
            _globalSettings = settings.Value;
            _xmlExportSerivce = xmlExportSerivce;
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
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while getting LastSynchronisation");
            }
            if (startDate == null) startDate = DateTime.Now.Date;

            // Get all new files from known locations
            List<DriveItem> newItems;
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
            foreach (var newItem in newItems)
            {
                try
                {
                    ObjectIdentifiersEntity? objectIdentifiersEntity;
                    try
                    {
                        objectIdentifiersEntity = await GetObjectIdentifiersEntityAsync(newItem.Id);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Error while getting objectIdentifiers");
                    }
                    if (objectIdentifiersEntity == null) throw new Exception("Error while getting objectIdentifiers");
                    string fileLocation = await UploadFileAndXmlToFileStorage(objectIdentifiersEntity, newItem.Name);

                    // Callback for changed file.
                    if (!fileLocation.IsNullOrEmpty() && !_globalSettings.CallbackUrl.IsNullOrEmpty())
                    {
                        CpsFile fileInfo = await _filesRepository.GetFileAsync(objectIdentifiersEntity.ObjectId);
                        //fileInfo.Metadata.FileLocation = fileLocation;
                        string body = JsonSerializer.Serialize(fileInfo.Metadata);
                        string callbackUrl = _globalSettings.CallbackUrl + $"/create/{objectIdentifiersEntity.ObjectId}";

                        await CallCallbackUrl(callbackUrl, body);
                    }
                }
                catch (Exception ex)
                {
                    // TODO: Log failed synchronisation.
                }
            }

            return Ok();
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
            List<DriveItem> updatedItems;
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
            foreach (var updatedItem in updatedItems)
            {
                try
                {
                    ObjectIdentifiersEntity? objectIdentifiersEntity;
                    try
                    {
                        objectIdentifiersEntity = await GetObjectIdentifiersEntityAsync(updatedItem.Id);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Error while getting objectIdentifiers");
                    }
                    if (objectIdentifiersEntity == null) throw new Exception("Error while getting objectIdentifiers");
                    string fileLocation = await UploadFileAndXmlToFileStorage(objectIdentifiersEntity, updatedItem.Name);

                    // Callback for changed file.
                    if (!fileLocation.IsNullOrEmpty() && !_globalSettings.CallbackUrl.IsNullOrEmpty())
                    {
                        CpsFile fileInfo = await _filesRepository.GetFileAsync(objectIdentifiersEntity.ObjectId);
                        //fileInfo.Metadata.FileLocation = fileLocation;
                        string body = JsonSerializer.Serialize(fileInfo.Metadata);
                        string callbackUrl = _globalSettings.CallbackUrl + $"/update/{objectIdentifiersEntity.ObjectId}";

                        await CallCallbackUrl(callbackUrl, body);
                    }
                }
                catch (Exception ex)
                {
                    // TODO: Log failed synchronisation.
                }
            }

            return Ok();
        }

        private async Task<string> UploadFileAndXmlToFileStorage(ObjectIdentifiersEntity objectIdentifiersEntity, string name)
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
                return null;
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

            var fileName = $"{objectIdentifiersEntity.ObjectId}.{name}";
            var fileLocation = "";
            try
            {
                await _fileStorageService.CreateAsync(Helpers.Constants.ContentContainerName, fileName, stream, metadata.MimeType, objectIdentifiersEntity.ObjectId);
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

            var metadataName = fileName + ".xml";
            try
            {
                await _fileStorageService.CreateAsync(Helpers.Constants.ContentContainerName, metadataName, metadataXml, "application/xml", objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while uploading metadata");
            }

            return fileLocation;
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
            List<DriveItem> deletedItems;
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
            foreach (var deletedItem in deletedItems)
            {
                try
                {
                    ObjectIdentifiersEntity? objectIdentifiersEntity;
                    try
                    {
                        objectIdentifiersEntity = await GetObjectIdentifiersEntityAsync(deletedItem.Id);
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
                        string callbackUrl = _globalSettings.CallbackUrl + $"/delete/{objectIdentifiersEntity.ObjectId}";
                        await CallCallbackUrl(callbackUrl);
                    }
                }
                catch (Exception ex)
                {
                    // TODO: Log failed synchronisation.
                }
            }

            return Ok();
        }

        private async Task CallCallbackUrl(string url, string body = "")
        {
            using (var client = new HttpClient())
            {
                try
                {
                    if (string.IsNullOrEmpty(body))
                    {
                        await client.GetAsync(url);
                    }
                    else
                    {
                        var content = new StringContent(body, Encoding.UTF8, "application/json");
                        await client.PostAsync(url, content);
                    }
                }
                catch (Exception ex)
                {
                    // Log error to callback service, otherwise ignore it
                }
            }
        }

        private async Task DeleteFileAndXmlFromFileStorage(ObjectIdentifiersEntity objectIdentifiersEntity)
        {
            try
            {
                await _fileStorageService.DeleteAsync(Helpers.Constants.ContentContainerName, objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while deleting document");
            }
        }

        private CloudTable? GetObjectIdentifiersTable()
        {
            var table = _storageTableService.GetTable(Helpers.Constants.ObjectIdentifiersTableName);
            if (table == null)
            {
                throw new Exception($"Tabel \"{Helpers.Constants.ObjectIdentifiersTableName}\" not found");
            }
            return table;
        }

        private async Task<ObjectIdentifiersEntity?> GetObjectIdentifiersEntityAsync(string driveItemId)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();

            var filter = TableQuery.GenerateFilterCondition("DriveItemId", QueryComparisons.Equal, driveItemId);
            var query = new TableQuery<ObjectIdentifiersEntity>().Where(filter);

            var result = await objectIdentifiersTable.ExecuteQuerySegmentedAsync(query, null);
            return result.Results?.FirstOrDefault();
        }
    }
}