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
            // Other synchronisation still running?
            bool? isSynchronisationRunning;
            try
            {
                isSynchronisationRunning = await _settingsRepository.GetIsSynchronisationRunningAsync(_globalSettings.SettingsIsNewSynchronisationRunningRowKey);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error while getting IsNewSynchronisationRunning");
            }
            if (isSynchronisationRunning == true)
            {
                return Ok("Other new synchronisation job is running.");
            }

            // Set running synchronisation.
            var setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsIsNewSynchronisationRunningRowKey);
            setting.IsNewSynchronisationRunning = true;
            await _settingsRepository.SaveSettingAsync(setting);

            // Get last synchronisation token.
            Dictionary<string, string> tokens;
            try
            {
                tokens = await _settingsRepository.GetLastTokensAsync(_globalSettings.SettingsLastTokenForNewRowKey);
            }
            catch (Exception ex)
            {
                NewSynchronisationStopped();
                return StatusCode(500, "Error while getting LastTokenForNew");
            }

            // Get last synchronisation date.
            DateTime? lastSynchronisation;
            try
            {
                lastSynchronisation = await _settingsRepository.GetLastSynchronisationAsync(_globalSettings.SettingsLastSynchronisationNewRowKey);
            }
            catch (Exception ex)
            {
                NewSynchronisationStopped();
                return StatusCode(500, ex.Message ?? "Error while getting LastSynchronisation");
            }
            if (lastSynchronisation == null) lastSynchronisation = DateTime.Now.Date;

            // Get all new files from known locations
            DeltaResponse deltaResponse;
            try
            {
                deltaResponse = await _driveRepository.GetNewItems(lastSynchronisation.Value, tokens);
            }
            catch (Exception ex)
            {
                NewSynchronisationStopped();
                return StatusCode(500, ex.Message ?? "Error while getting new documents");
            }
            if (deltaResponse == null)
            {
                NewSynchronisationStopped();
                return StatusCode(500, "Error while getting new documents");
            }

            // For each file:
            // generate xml from metadata
            // upload file to storage container
            // upload xml to storage container
            var itemsAdded = 0;
            var notAddedItems = new List<DeltaDriveItem>();
            foreach (var newItem in deltaResponse.Items)
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

                    if (succeeded)
                    {
                        itemsAdded++;
                    }
                }
                catch (Exception ex)
                {
                    notAddedItems.Add(newItem);
                    _logger.LogError($"Error while adding file (DriveId: {newItem?.DriveId}, DriveItemId: {newItem?.Id}) to FileStorage: {ex.Message}");
                }
            }

            // If all files are succesfully added then we update the last synchronisation date.
            setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastSynchronisationNewRowKey);
            setting.LastSynchronisationNew = DateTime.UtcNow;
            await _settingsRepository.SaveSettingAsync(setting);

            // If all files are succesfully added then we update the token.
            setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastTokenForNewRowKey);
            setting.LastTokenForNew = string.Join(";", deltaResponse.NextTokens.Select(x => x.Key + "=" + x.Value).ToArray());
            await _settingsRepository.SaveSettingAsync(setting);

            NewSynchronisationStopped();

            var notDeletedItemsAsStr = notAddedItems.Select(item => $"Error while adding file (DriveId: {item.DriveId}, DriveItemId: {item.Id}) to FileStorage.").ToList();
            var message = String.Join("\r\n", notDeletedItemsAsStr.Select(x => x.ToString()).ToArray());
            message = $"{itemsAdded} items added" + (notDeletedItemsAsStr.Any() ? "\r\n" : "") + message;
            return Ok(message);
        }

        private async Task NewSynchronisationStopped()
        {
            var setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsIsNewSynchronisationRunningRowKey);
            setting.IsNewSynchronisationRunning = false;
            await _settingsRepository.SaveSettingAsync(setting);
        }

        [HttpGet]
        [Route("updated")]
        public async Task<IActionResult> SynchroniseUpdatedDocuments()
        {
            // Other synchronisation still running?
            bool? isSynchronisationRunning;
            try
            {
                isSynchronisationRunning = await _settingsRepository.GetIsSynchronisationRunningAsync(_globalSettings.SettingsIsChangedSynchronisationRunningRowKey);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error while getting IsChangedSynchronisationRunning");
            }
            if (isSynchronisationRunning == true)
            {
                return Ok("Other update synchronisation job is running.");
            }

            // Set running synchronisation.
            var setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsIsChangedSynchronisationRunningRowKey);
            setting.IsChangedSynchronisationRunning = true;
            await _settingsRepository.SaveSettingAsync(setting);

            // Get last synchronisation token.
            Dictionary<string, string> tokens;
            try
            {
                tokens = await _settingsRepository.GetLastTokensAsync(_globalSettings.SettingsLastTokenForChangedRowKey);
            }
            catch (Exception ex)
            {
                ChangedSynchronisationStopped();
                return StatusCode(500, "Error while getting LastTokenForChanged");
            }

            // Get last synchronisation date.
            DateTime? lastSynchronisation;
            try
            {
                lastSynchronisation = await _settingsRepository.GetLastSynchronisationAsync(_globalSettings.SettingsLastSynchronisationChangedRowKey);
            }
            catch (Exception ex)
            {
                ChangedSynchronisationStopped();
                return StatusCode(500, "Error while getting LastSynchronisation");
            }
            if (lastSynchronisation == null) lastSynchronisation = DateTime.Now.Date;

            // Get all updated files from known locations
            DeltaResponse deltaResponse;
            try
            {
                deltaResponse = await _driveRepository.GetUpdatedItems(lastSynchronisation.Value, tokens);
            }
            catch (Exception ex)
            {
                ChangedSynchronisationStopped();
                return StatusCode(500, "Error while getting updated documents");
            }
            if (deltaResponse == null)
            {
                ChangedSynchronisationStopped();
                return StatusCode(500, "Error while getting updated documents");
            }

            // For each file:
            // generate xml from metadata
            // upload file to storage container
            // upload xml to storage container
            var itemsUpdated = 0;
            var notUpdatedItems = new List<DeltaDriveItem>();
            foreach (var updatedItem in deltaResponse.Items)
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

                    if (succeeded)
                    {
                        itemsUpdated++;
                    }
                }
                catch (Exception ex)
                {
                    notUpdatedItems.Add(updatedItem);
                    _logger.LogError($"Error while updating file (DriveId: {updatedItem?.DriveId}, DriveItemId: {updatedItem?.Id}) in FileStorage: {ex.Message}");
                }
            }

            // If all files are succesfully updated then we update the last synchronisation date.           
            setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastSynchronisationChangedRowKey);
            setting.LastSynchronisationChanged = DateTime.UtcNow;
            await _settingsRepository.SaveSettingAsync(setting);

            // If all files are succesfully updated then we update the token.
            setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastTokenForChangedRowKey);
            setting.LastTokenForChanged = string.Join(";", deltaResponse.NextTokens.Select(x => x.Key + "=" + x.Value).ToArray());
            await _settingsRepository.SaveSettingAsync(setting);

            ChangedSynchronisationStopped();

            var notDeletedItemsAsStr = notUpdatedItems.Select(item => $"Error while updating file (DriveId: {item.DriveId}, DriveItemId: {item.Id}) in FileStorage.\r\n").ToList();
            var message = String.Join(",", notDeletedItemsAsStr.Select(x => x.ToString()).ToArray());
            message = $"{itemsUpdated} items updated" + (notDeletedItemsAsStr.Any() ? "\r\n" : "") + message;
            return Ok(message);
        }

        private async Task ChangedSynchronisationStopped()
        {
            var setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsIsChangedSynchronisationRunningRowKey);
            setting.IsChangedSynchronisationRunning = false;
            await _settingsRepository.SaveSettingAsync(setting);
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
            // Other synchronisation still running?
            bool? isSynchronisationRunning;
            try
            {
                isSynchronisationRunning = await _settingsRepository.GetIsSynchronisationRunningAsync(_globalSettings.SettingsIsDeletedSynchronisationRunningRowKey);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error while getting IsDeleteddSynchronisationRunning");
            }
            if (isSynchronisationRunning == true)
            {
                return Ok("Other delete synchronisation job is running.");
            }

            // Set running synchronisation.
            var setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsIsDeletedSynchronisationRunningRowKey);
            setting.IsDeletedSynchronisationRunning = true;
            await _settingsRepository.SaveSettingAsync(setting);

            // Get last synchronisation token.
            Dictionary<string, string> tokens;
            try
            {
                tokens = await _settingsRepository.GetLastTokensAsync(_globalSettings.SettingsLastTokenForDeletedRowKey);
            }
            catch (Exception ex)
            {
                DeletedSynchronisationStopped();
                return StatusCode(500, "Error while getting LastTokenForDeleted");
            }

            // Get all deleted files from known locations
            DeltaResponse deltaResponse;
            try
            {
                deltaResponse = await _driveRepository.GetDeletedItems(tokens);
            }
            catch (Exception ex)
            {
                DeletedSynchronisationStopped();
                return StatusCode(500, "Error while getting deleted documents");
            }
            if (deltaResponse == null)
            {
                DeletedSynchronisationStopped();
                return StatusCode(500, "Error while getting deleted documents");
            }

            // For each file:
            // delete file from storage container
            // delete xml from storage container
            var itemsDeleted = 0;
            var notDeletedItems = new List<DeltaDriveItem>();
            foreach (var deletedItem in deltaResponse.Items)
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
                    itemsDeleted++;
                }
                catch (Exception ex)
                {
                    notDeletedItems.Add(deletedItem);
                    _logger.LogError($"Error while deleting file (DriveId: {deletedItem?.DriveId}, DriveItemId: {deletedItem?.Id}) from FileStorage: {ex.Message}");
                }
            }

            // If all files are succesfully deleted then we update the token.
            setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsLastTokenForDeletedRowKey);
            setting.LastTokenForDeleted = string.Join(";", deltaResponse.NextTokens.Select(x => x.Key + "=" + x.Value).ToArray()); ;
            await _settingsRepository.SaveSettingAsync(setting);

            DeletedSynchronisationStopped();

            var notDeletedItemsAsStr = notDeletedItems.Select(item => $"Error while deleting file (DriveId: {item.DriveId}, DriveItemId: {item.Id}) from FileStorage.\r\n").ToList();
            var message = String.Join(",", notDeletedItemsAsStr.Select(x => x.ToString()).ToArray());
            message = $"{itemsDeleted} items deleted" + (notDeletedItemsAsStr.Any() ? "\r\n" : "") + message;
            return Ok(message);
        }

        private async Task DeletedSynchronisationStopped()
        {
            var setting = new SettingsEntity(_globalSettings.SettingsPartitionKey, _globalSettings.SettingsIsDeletedSynchronisationRunningRowKey);
            setting.IsDeletedSynchronisationRunning = false;
            await _settingsRepository.SaveSettingAsync(setting);
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
                        var sb = new StringBuilder();
                        sb.Append("Error while sending sync callback");
                        sb.Append("Request:");
                        sb.Append(request.ToString());
                        sb.Append("Body: ");
                        sb.Append(body);
                        sb.Append("Response:");
                        sb.Append(response.ToString());
                        _logger.LogError(sb.ToString());
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