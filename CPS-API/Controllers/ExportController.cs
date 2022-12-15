using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using Microsoft.WindowsAzure.Storage.Table;
using System.Reflection;
using System.Text;

namespace CPS_API.Controllers
{
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

        public ExportController(IDriveRepository driveRepository,
                                ISettingsRepository settingsRepository,
                                FileStorageService fileStorageService,
                                StorageTableService storageTableService,
                                IFilesRepository filesRepository,
                                IOptions<GlobalSettings> settings)
        {
            _driveRepository = driveRepository;
            _settingsRepository = settingsRepository;
            _fileStorageService = fileStorageService;
            _storageTableService = storageTableService;
            _filesRepository = filesRepository;
            _globalSettings = settings.Value;
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
                startDate = await _settingsRepository.GetLastSynchronisationAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error while getting LastSynchronisation");
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
                return StatusCode(500, "Error while getting new documents");
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
                    await uploadFileAndXmlToFileStorage(newItem);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }

                // Callback for new file.
                if (!_globalSettings.CallbackUrl.IsNullOrEmpty())
                {
                    dynamic callbackFunc = _globalSettings.CallbackUrl;
                    callbackFunc();
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
                startDate = await _settingsRepository.GetLastSynchronisationAsync();
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
                    await uploadFileAndXmlToFileStorage(updatedItem);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }

                // Callback for changed file.
                if (!_globalSettings.CallbackUrl.IsNullOrEmpty())
                {
                    dynamic callbackFunc = _globalSettings.CallbackUrl;
                    callbackFunc();
                }
            }

            return Ok();
        }

        private async Task uploadFileAndXmlToFileStorage(DriveItem driveItem)
        {
            ObjectIdentifiersEntity objectIdentifiersEntity;
            try
            {
                objectIdentifiersEntity = await GetObjectIdentifiersEntityAsync(driveItem.Id);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while getting objectIdentifiers");
            }
            if (objectIdentifiersEntity == null) throw new Exception("Error while getting objectIdentifiers");

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
                stream = await _driveRepository.DownloadAsync(objectIdentifiersEntity.DriveId, driveItem.Id);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while getting content");
            }
            if (stream == null) throw new Exception("Error while getting content");

            var fileName = $"{objectIdentifiersEntity.ObjectId}.{driveItem.Name}";
            bool succeeded;
            try
            {
                succeeded = await _fileStorageService.CreateAsync(Helpers.Constants.ContentContainerName, fileName, stream, metadata.MimeType);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while uploading document");
            }
            if (!succeeded) throw new Exception("Error while uploading document");

            string metadataXml;
            try
            {
                metadataXml = exportMetadataAsXml(metadata);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while exporting metadata to xml");
            }

            var metadataName = fileName + ".xml";
            try
            {
                succeeded = await _fileStorageService.CreateAsync(Helpers.Constants.MetadataContainerName, metadataName, metadataXml, "application/xml");
            }
            catch (Exception ex)
            {
                throw new Exception("Error while uploading metadata");
            }
            if (!succeeded) throw new Exception("Error while uploading metadata");
        }

        [HttpGet]
        [Route("deleted")]
        public async Task<IActionResult> SynchroniseDeletedDocuments()
        {
            // Get last synchronisation date.
            DateTime? startDate;
            try
            {
                startDate = await _settingsRepository.GetLastSynchronisationAsync();
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
                ObjectIdentifiersEntity? objectIdentifiersEntity;
                try
                {
                    objectIdentifiersEntity = await GetObjectIdentifiersEntityAsync(deletedItem.Id);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, "Error while getting objectIdentifiers");
                }
                if (objectIdentifiersEntity == null) return StatusCode(500, "Error while getting objectIdentifiers");

                var fileName = $"{objectIdentifiersEntity.ObjectId}.{deletedItem.Name}";
                bool succeeded;
                try
                {
                    succeeded = await _fileStorageService.DeleteAsync(Helpers.Constants.ContentContainerName, fileName);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, "Error while deleting document");
                }
                if (!succeeded) return StatusCode(500, "Error while deleting document");

                var metadataName = fileName + ".xml";
                try
                {
                    succeeded = await _fileStorageService.DeleteAsync(Helpers.Constants.MetadataContainerName, metadataName);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, "Error while deleting metadata");
                }
                if (!succeeded) return StatusCode(500, "Error while deleting metadata");

                // Callback for deleted file.
                if (!_globalSettings.CallbackUrl.IsNullOrEmpty())
                {
                    dynamic callbackFunc = _globalSettings.CallbackUrl;
                    callbackFunc();
                }
            }

            return Ok();
        }

        private string exportMetadataAsXml(FileInformation metadata)
        {
            var xml = new StringBuilder();
            foreach (var propertyInfo in metadata.GetType().GetProperties())
            {
                if (propertyInfo.PropertyType == typeof(FileMetadata))
                {
                    var value = propertyInfo.GetValue(metadata);
                    if (value == null) throw new ArgumentNullException(nameof(value));
                    foreach (var secondPropertyInfo in value.GetType().GetProperties())
                    {
                        if (secondPropertyInfo.Name == "Item")
                        {
                            continue;
                        }
                        xml.AppendLine(GetPropertyXml(secondPropertyInfo, value));
                    }
                }
                else
                {
                    xml.AppendLine(GetPropertyXml(propertyInfo, metadata));
                }
            }
            return $"<?xml version=\"1.0\"?><document id=\"{metadata.Ids.ObjectId}\">{xml}</document></xml>";
        }

        private string GetPropertyXml(PropertyInfo? propertyInfo, object obj)
        {
            if (propertyInfo == null) throw new ArgumentNullException(nameof(propertyInfo));
            var value = propertyInfo.GetValue(obj);
            if (value == null) throw new ArgumentNullException(nameof(propertyInfo));
            var valueAsStr = value.ToString();

            var propertyName = propertyInfo.Name;
            if (propertyName == null) throw new ArgumentNullException(nameof(propertyInfo));
            propertyName = FirstCharToLowerCase(propertyName);

            return $"<{propertyName}>{valueAsStr}</{propertyName}>";
        }
        public string? FirstCharToLowerCase(string? str)
        {
            if (!string.IsNullOrEmpty(str) && char.IsUpper(str[0]))
                return str.Length == 1 ? char.ToLower(str[0]).ToString() : char.ToLower(str[0]) + str[1..];

            return str;
        }

        private CloudTable? GetObjectIdentifiersTable()
        {
            return _storageTableService.GetTable(Helpers.Constants.ObjectIdentifiersTableName);
        }

        private async Task<ObjectIdentifiersEntity?> GetObjectIdentifiersEntityAsync(string driveItemId)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();
            if (objectIdentifiersTable == null) throw new ArgumentNullException(nameof(objectIdentifiersTable));

            var filter = TableQuery.GenerateFilterCondition("DriveItemId", QueryComparisons.Equal, driveItemId);
            var query = new TableQuery<ObjectIdentifiersEntity>().Where(filter);

            var result = await objectIdentifiersTable.ExecuteQuerySegmentedAsync(query, null);
            return result.Results?.FirstOrDefault();
        }
    }
}