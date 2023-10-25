using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Repositories;
using CPS_API.Services;
using Microsoft.ApplicationInsights;
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
        private readonly IFilesRepository _filesRepository;
        private readonly IMetadataRepository _sharePointRepository;

        private readonly FileStorageService _fileStorageService;
        private readonly StorageTableService _storageTableService;
        private readonly XmlExportSerivce _xmlExportSerivce;

        private readonly GlobalSettings _globalSettings;

        private readonly TelemetryClient _telemetryClient;

        public ExportController(IDriveRepository driveRepository,
                                ISettingsRepository settingsRepository,
                                FileStorageService fileStorageService,
                                StorageTableService storageTableService,
                                IFilesRepository filesRepository,
                                IOptions<GlobalSettings> settings,
                                XmlExportSerivce xmlExportSerivce,
                                TelemetryClient telemetryClient,
                                IMetadataRepository sharePointRepository)
        {
            _driveRepository = driveRepository;
            _settingsRepository = settingsRepository;
            _fileStorageService = fileStorageService;
            _storageTableService = storageTableService;
            _filesRepository = filesRepository;
            _globalSettings = settings.Value;
            _xmlExportSerivce = xmlExportSerivce;
            _telemetryClient = telemetryClient;
            _sharePointRepository = sharePointRepository;
        }

        // GET
        [HttpGet]
        [Route("new")]
        public async Task<IActionResult> SynchroniseNewDocuments()
        {
            // Other synchronisation still running?
            try
            {
                bool? isSynchronisationRunning = await _settingsRepository.GetSetting<bool?>(Constants.SettingsIsNewSynchronisationRunningField);
                if (isSynchronisationRunning.HasValue && isSynchronisationRunning.Value) return Ok("Other new synchronisation job is running.");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                return StatusCode(500, "Error while getting IsNewSynchronisationRunning");
            }

            // Set running synchronisation.
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsNewSynchronisationRunningField, true);

            // Get last synchronisation token.
            Dictionary<string, string> tokens;
            try
            {
                tokens = await _settingsRepository.GetLastTokensAsync(Constants.SettingsLastTokenForNewField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await NewSynchronisationStopped();
                return StatusCode(500, "Error while getting LastTokenForNew");
            }

            // Get last synchronisation date.
            DateTime? lastSynchronisation;
            try
            {
                lastSynchronisation = await _settingsRepository.GetSetting<DateTime?>(Constants.SettingsLastSynchronisationNewField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await NewSynchronisationStopped();
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
                _telemetryClient.TrackException(ex);
                await NewSynchronisationStopped();
                return StatusCode(500, ex.Message ?? "Error while getting new documents");
            }
            if (deltaResponse == null)
            {
                _telemetryClient.TrackException(new CpsException("Delta response is null"));
                await NewSynchronisationStopped();
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
                        throw new CpsException("Error while getting objectIdentifiers", ex);
                    }
                    if (objectIdentifiersEntity == null) throw new CpsException("Error while getting objectIdentifiers");
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
                    var properties = new Dictionary<string, string>
                    {
                        ["DriveId"] = newItem?.DriveId,
                        ["DriveItemId"] = newItem?.Id
                    };

                    _telemetryClient.TrackException(ex, properties);
                    notAddedItems.Add(newItem);
                }
            }

            // If all files are succesfully added then we update the last synchronisation date.
            await _settingsRepository.SaveSettingAsync(Constants.SettingsLastSynchronisationNewField, DateTime.UtcNow);

            // If all files are succesfully added then we update the token.
            string lastTokenForNew = string.Join(";", deltaResponse.NextTokens.Select(x => x.Key + "=" + x.Value).ToArray());
            await _settingsRepository.SaveSettingAsync(Constants.SettingsLastTokenForNewField, lastTokenForNew);

            await NewSynchronisationStopped();

            var notDeletedItemsAsStr = notAddedItems.Select(item => $"Error while adding file (DriveId: {item.DriveId}, DriveItemId: {item.Id}) to FileStorage.").ToList();
            var message = String.Join("\r\n", notDeletedItemsAsStr.Select(x => x.ToString()).ToArray());
            message = $"{itemsAdded} items added" + (notDeletedItemsAsStr.Any() ? "\r\n" : "") + message;
            return Ok(message);
        }

        private async Task NewSynchronisationStopped()
        {
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsNewSynchronisationRunningField, false);
            _telemetryClient.TrackEvent("New item synchronisation has stopped");
        }

        [HttpGet]
        [Route("updated")]
        public async Task<IActionResult> SynchroniseUpdatedDocuments()
        {
            // Other synchronisation still running?
            try
            {
                bool? isSynchronisationRunning = await _settingsRepository.GetSetting<bool?>(Constants.SettingsIsChangedSynchronisationRunningField);
                if (isSynchronisationRunning.HasValue && isSynchronisationRunning.Value)
                {
                    return Ok("Other update synchronisation job is running.");
                }
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                return StatusCode(500, "Error while getting IsChangedSynchronisationRunning");
            }

            // Set running synchronisation.
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsChangedSynchronisationRunningField, true);

            // Get last synchronisation token.
            Dictionary<string, string> tokens;
            try
            {
                tokens = await _settingsRepository.GetLastTokensAsync(Constants.SettingsLastTokenForChangedField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await ChangedSynchronisationStopped();
                return StatusCode(500, "Error while getting LastTokenForChanged");
            }

            // Get last synchronisation date.
            DateTime? lastSynchronisation;
            try
            {
                lastSynchronisation = await _settingsRepository.GetSetting<DateTime?>(Constants.SettingsLastSynchronisationChangedField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await ChangedSynchronisationStopped();
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
                _telemetryClient.TrackException(ex);
                await ChangedSynchronisationStopped();
                return StatusCode(500, "Error while getting updated documents");
            }
            if (deltaResponse == null)
            {
                _telemetryClient.TrackException(new CpsException("Delta response is null"));
                await ChangedSynchronisationStopped();
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
                        throw new CpsException("Error while getting objectIdentifiers", ex);
                    }
                    if (objectIdentifiersEntity == null) throw new CpsException("Error while getting objectIdentifiers");
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
                    var properties = new Dictionary<string, string>
                    {
                        ["DriveId"] = updatedItem?.DriveId,
                        ["DriveItemId"] = updatedItem?.Id
                    };

                    _telemetryClient.TrackException(ex, properties);
                    _telemetryClient.TrackEvent($"Error while updating file (DriveId: {updatedItem?.DriveId}, DriveItemId: {updatedItem?.Id}) in FileStorage: {ex.Message}");
                    notUpdatedItems.Add(updatedItem);
                }
            }

            // If all files are succesfully updated then we update the last synchronisation date.      
            await _settingsRepository.SaveSettingAsync(Constants.SettingsLastSynchronisationChangedField, DateTime.UtcNow);

            // If all files are succesfully updated then we update the token.
            string lastToken = string.Join(";", deltaResponse.NextTokens.Select(x => x.Key + "=" + x.Value).ToArray());
            await _settingsRepository.SaveSettingAsync(Constants.SettingsLastTokenForChangedField, lastToken);

            await ChangedSynchronisationStopped();

            var notDeletedItemsAsStr = notUpdatedItems.Select(item => $"Error while updating file (DriveId: {item.DriveId}, DriveItemId: {item.Id}) in FileStorage.\r\n").ToList();
            var message = String.Join(",", notDeletedItemsAsStr.Select(x => x.ToString()).ToArray());
            message = $"{itemsUpdated} items updated" + (notDeletedItemsAsStr.Any() ? "\r\n" : "") + message;
            return Ok(message);
        }

        private async Task ChangedSynchronisationStopped()
        {
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsChangedSynchronisationRunningField, false);
            _telemetryClient.TrackEvent("Changed item synchronisation has stopped");
        }

        private async Task<bool> UploadFileAndXmlToFileStorage(ObjectIdentifiersEntity objectIdentifiersEntity, string name)
        {
            bool metadataExists;
            try
            {
                var ids = new ObjectIdentifiers(objectIdentifiersEntity);
                metadataExists = await _sharePointRepository.FileContainsMetadata(ids);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting metadata", ex);
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
                metadata = await _sharePointRepository.GetMetadataAsync(objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting metadata", ex);
            }
            if (metadata == null) throw new CpsException("Error while getting metadata");

            Stream? stream;
            try
            {
                stream = await _driveRepository.GetStreamAsync(objectIdentifiersEntity.DriveId, objectIdentifiersEntity.DriveItemId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting content", ex);
            }
            if (stream == null) throw new CpsException("Error while getting content");

            var fileName = objectIdentifiersEntity.ObjectId + "." + metadata.FileExtension;
            try
            {
                await _fileStorageService.CreateAsync(_globalSettings.ContentContainerName, fileName, stream, metadata.MimeType, objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while uploading document", ex);
            }

            string metadataXml;
            try
            {
                metadataXml = _xmlExportSerivce.GetMetadataAsXml(metadata);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while exporting metadata to xml", ex);
            }

            var metadataName = objectIdentifiersEntity.ObjectId + ".xml";
            try
            {
                await _fileStorageService.CreateAsync(_globalSettings.MetadataContainerName, metadataName, metadataXml, "application/xml", objectIdentifiersEntity.ObjectId);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while uploading metadata", ex);
            }

            return true;
        }

        [HttpGet]
        [Route("deleted")]
        public async Task<IActionResult> SynchroniseDeletedDocuments()
        {
            // Other synchronisation still running?
            try
            {
                bool? isSynchronisationRunning = await _settingsRepository.GetSetting<bool?>(Constants.SettingsIsDeletedSynchronisationRunningField);
                if (isSynchronisationRunning.HasValue && isSynchronisationRunning.Value)
                {
                    return Ok("Other delete synchronisation job is running.");
                }
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                return StatusCode(500, "Error while getting IsDeleteddSynchronisationRunning");
            }

            // Set running synchronisation.
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsDeletedSynchronisationRunningField, true);

            // Get last synchronisation token.
            Dictionary<string, string> tokens;
            try
            {
                tokens = await _settingsRepository.GetLastTokensAsync(Constants.SettingsLastTokenForDeletedField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await DeletedSynchronisationStopped();
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
                _telemetryClient.TrackException(ex);
                await DeletedSynchronisationStopped();
                return StatusCode(500, "Error while getting deleted documents");
            }
            if (deltaResponse == null)
            {
                _telemetryClient.TrackException(new CpsException("Delta response is null"));
                await DeletedSynchronisationStopped();
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
                        throw new CpsException("Error while getting objectIdentifiers", ex);
                    }
                    if (objectIdentifiersEntity == null) throw new CpsException("Error while getting objectIdentifiers");
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
                    var properties = new Dictionary<string, string>
                    {
                        ["DriveId"] = deletedItem?.DriveId,
                        ["DriveItemId"] = deletedItem?.Id
                    };

                    _telemetryClient.TrackException(ex, properties);
                    _telemetryClient.TrackEvent($"Error while deleting file (DriveId: {deletedItem?.DriveId}, DriveItemId: {deletedItem?.Id}) from FileStorage: {ex.Message}");
                    notDeletedItems.Add(deletedItem);
                }
            }

            // If all files are succesfully deleted then we update the token.
            string lastToken = string.Join(";", deltaResponse.NextTokens.Select(x => x.Key + "=" + x.Value).ToArray());
            await _settingsRepository.SaveSettingAsync(Constants.SettingsLastTokenForDeletedField, lastToken);

            await DeletedSynchronisationStopped();

            var notDeletedItemsAsStr = notDeletedItems.Select(item => $"Error while deleting file (DriveId: {item.DriveId}, DriveItemId: {item.Id}) from FileStorage.\r\n").ToList();
            var message = String.Join(",", notDeletedItemsAsStr.Select(x => x.ToString()).ToArray());
            message = $"{itemsDeleted} items deleted" + (notDeletedItemsAsStr.Any() ? "\r\n" : "") + message;
            return Ok(message);
        }

        private async Task DeletedSynchronisationStopped()
        {
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsDeletedSynchronisationRunningField, false);
            _telemetryClient.TrackEvent("Deleted item synchronisation has stopped");
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
                        string responseContent = "";
                        if (response.Content != null)
                        {
                            try
                            {
                                responseContent = await response.Content.ReadAsStringAsync();
                            }
                            catch { }
                        }

                        var properties = new Dictionary<string, string>
                        {
                            ["Body"] = body,
                            ["Request"] = request.ToString(),
                            ["Response"] = response.ToString(),
                            ["ResponseBody"] = responseContent,
                        };

                        _telemetryClient.TrackException(new CpsException("Callback failed"), properties);
                    }
                }
                catch (Exception ex)
                {
                    var properties = new Dictionary<string, string>
                    {
                        ["Body"] = body
                    };

                    // Log error, otherwise ignore it
                    _telemetryClient.TrackException(ex, properties);
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
                throw new CpsException("Error while deleting document", ex);
            }
        }

        private CloudTable GetObjectIdentifiersTable()
        {
            var table = _storageTableService.GetTable(_globalSettings.ObjectIdentifiersTableName);
            if (table == null)
            {
                throw new CpsException($"Tabel \"{_globalSettings.ObjectIdentifiersTableName}\" not found");
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