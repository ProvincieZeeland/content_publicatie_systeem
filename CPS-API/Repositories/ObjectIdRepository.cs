using System.Net;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IObjectIdRepository
    {
        Task<string> GenerateObjectIdAsync(ObjectIdentifiers ids, bool getAsUser = false);

        Task<ObjectIdentifiersEntity?> GetObjectIdentifiersAsync(string objectId);

        Task<string?> GetObjectIdAsync(ObjectIdentifiers ids);

        Task SaveObjectIdentifiersAsync(string objectId, ObjectIdentifiers ids);

        Task SaveAdditionalIdentifiersAsync(string objectId, List<string> additionalIds);

        Task<ObjectIdentifiers> FindMissingIds(ObjectIdentifiers ids, bool getAsUser = false);
    }

    public class ObjectIdRepository : IObjectIdRepository
    {
        private const string prefix = "ZLD";
        private const string seperator = ";";
        private readonly ISettingsRepository _settingsRepository;
        private readonly StorageTableService _storageTableService;
        private readonly IDriveRepository _driveRepository;
        private readonly GlobalSettings _globalSettings;

        public ObjectIdRepository(ISettingsRepository settingsRepository,
                                   StorageTableService storageTableService,
                                   IDriveRepository driveRepository,
                                   IOptions<GlobalSettings> settings)
        {
            _settingsRepository = settingsRepository;
            _storageTableService = storageTableService;
            _driveRepository = driveRepository;
            _globalSettings = settings.Value;
        }

        public async Task<string> GenerateObjectIdAsync(ObjectIdentifiers ids, bool getAsUser = false)
        {
            // Add any missing location IDs before looking for existing.
            ids = await FindMissingIds(ids, getAsUser);

            // Check if the ID's are valid.
            if (ids.SiteId.IsNullOrEmpty())
            {
                throw new CpsException(nameof(ids.SiteId) + " not found");
            }
            if (ids.ListId.IsNullOrEmpty())
            {
                throw new CpsException(nameof(ids.ListId) + " not found");
            }
            if (ids.ListItemId.IsNullOrEmpty())
            {
                throw new CpsException(nameof(ids.ListItemId) + " not found");
            }
            if (ids.DriveId.IsNullOrEmpty())
            {
                throw new CpsException(nameof(ids.DriveId) + " not found");
            }
            if (ids.DriveItemId.IsNullOrEmpty())
            {
                throw new CpsException(nameof(ids.DriveItemId) + " not found");
            }

            // Check if objectIdentifiers already in table, if so; return objectId.
            var existingObjectId = await GetObjectIdAsync(ids);
            if (existingObjectId != null)
            {
                return existingObjectId;
            }

            // Increase sequencenr and store in table
            long? sequence = null;
            try
            {
                sequence = await _settingsRepository.IncreaseSequenceNumberAsync();
            }
            catch (Exception ex)
            {
                throw new CpsException($"Error while saving new sequence {sequence}", ex);
            }

            // Create new objectId
            var objectId = $"{prefix}{DateTime.Now.Year}-{sequence}";
            ids.ObjectId = objectId;

            // Store objectId + backend ids in table
            try
            {
                await SaveObjectIdentifiersAsync(objectId, ids);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while saving SharePoint ids", ex);
            }

            return objectId;
        }

        public async Task<ObjectIdentifiers> FindMissingIds(ObjectIdentifiers ids, bool getAsUser = false)
        {
            if ((string.IsNullOrEmpty(ids.DriveId) || string.IsNullOrEmpty(ids.DriveItemId))
                && (!string.IsNullOrEmpty(ids.SiteId) && !string.IsNullOrEmpty(ids.ListId)))
            {
                // Find driveID for object
                try
                {
                    var drive = await _driveRepository.GetDriveAsync(ids.SiteId, ids.ListId, getAsUser);
                    ids.DriveId = drive.Id;
                }
                catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ex.Error?.Message == "Invalid hostname for this tenancy")
                {
                    throw new FileNotFoundException("The specified site was not found", ex);
                }
                catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new FileNotFoundException((ex.Error == null ? ex.Message : ex.Error.Message) ?? "Drive not found", ex);
                }
                catch (Exception ex)
                {
                    throw new CpsException("Error while getting driveId", ex);
                }

                // Find driveItemID for object
                try
                {
                    if (!string.IsNullOrEmpty(ids.ListItemId))
                    {
                        var driveItem = await _driveRepository.GetDriveItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, getAsUser: getAsUser);
                        ids.DriveItemId = driveItem.Id;
                    }
                }
                catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.Error?.Message == "Item not found")
                {
                    throw new FileNotFoundException("The specified listItem was not found", ex);
                }
                catch (Exception ex)
                {
                    throw new CpsException("Error while getting driveItemId", ex);
                }
            }

            if ((string.IsNullOrEmpty(ids.SiteId) || string.IsNullOrEmpty(ids.ListId) || string.IsNullOrEmpty(ids.ListItemId))
                && (!string.IsNullOrEmpty(ids.DriveId) && !string.IsNullOrEmpty(ids.DriveItemId)))
            {
                // Find sharepoint Ids from drive
                try
                {
                    var driveItem = await _driveRepository.GetDriveItemIdsAsync(ids.DriveId, ids.DriveItemId, getAsUser);
                    ids.SiteId = driveItem.SharepointIds.SiteId;
                    ids.ListId = driveItem.SharepointIds.ListId;
                    ids.ListItemId = driveItem.SharepointIds.ListItemId;
                }
                catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ex.Error?.Message == "The provided drive id appears to be malformed, or does not represent a valid drive.")
                {
                    throw new FileNotFoundException("The specified drive was not found", ex);
                }
                catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.Error?.Message == "Item not found")
                {
                    throw new FileNotFoundException("The specified driveItem was not found", ex);
                }
                catch (Exception ex)
                {
                    throw new CpsException("Error while getting SharePoint Ids", ex);
                }
            }

            if (!string.IsNullOrEmpty(ids.ObjectId) &&
                (string.IsNullOrEmpty(ids.SiteId) || string.IsNullOrEmpty(ids.ListId) || string.IsNullOrEmpty(ids.ListItemId)
                || string.IsNullOrEmpty(ids.DriveId) || string.IsNullOrEmpty(ids.DriveItemId)))
            {
                try
                {
                    var idsFromStorageTable = await this.GetObjectIdentifiersAsync(ids.ObjectId);
                    if (idsFromStorageTable == null) throw new CpsException("Identifiers not found");
                    ids = new ObjectIdentifiers(idsFromStorageTable);
                }
                catch (Exception ex)
                {
                    throw new CpsException("Error while getting SharePoint Ids", ex);
                }
            }

            if (ids.ExternalReferenceListId.IsNullOrEmpty())
            {
                var locationMapping = _globalSettings.LocationMapping.FirstOrDefault(item =>
                                        item.SiteId == ids.SiteId
                                        && item.ListId == ids.ListId
                                      );
                ids.ExternalReferenceListId = locationMapping?.ExternalReferenceListId;
            }

            return ids;
        }

        private CloudTable? GetObjectIdentifiersTable()
        {
            var table = _storageTableService.GetTable(_globalSettings.ObjectIdentifiersTableName);
            if (table == null)
            {
                throw new CpsException($"Tabel \"{_globalSettings.ObjectIdentifiersTableName}\" not found");
            }
            return table;
        }

        public async Task<ObjectIdentifiersEntity?> GetObjectIdentifiersAsync(string objectId)
        {
            var objectIdentifiersEntity = await GetObjectIdentifiersEntityByAdditionalIdsAsync(objectId);
            if (objectIdentifiersEntity == null) objectIdentifiersEntity = await GetObjectIdentifiersEntityByObjectIdAsync(objectId);

            if (objectIdentifiersEntity == null) throw new FileNotFoundException($"ObjectIdentifiersEntity (objectId = {objectId}) does not exist!");

            return objectIdentifiersEntity;
        }

        private async Task<ObjectIdentifiersEntity?> GetObjectIdentifiersEntityByAdditionalIdsAsync(string objectId)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();
            objectId = objectId.ToUpper();
            var filter = TableQuery.GenerateFilterCondition(nameof(ObjectIdentifiersEntity.AdditionalObjectIds), QueryComparisons.LessThan, objectId);
            var query = new TableQuery<ObjectIdentifiersEntity>().Where(filter);

            var result = await objectIdentifiersTable.ExecuteQuerySegmentedAsync(query, null);
            return result.Results?.FirstOrDefault();

        }

        private async Task<ObjectIdentifiersEntity?> GetObjectIdentifiersEntityByObjectIdAsync(string objectId)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();

            objectId = objectId.ToUpper();
            var filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, objectId);
            var query = new TableQuery<ObjectIdentifiersEntity>().Where(filter);

            var result = await objectIdentifiersTable.ExecuteQuerySegmentedAsync(query, null);
            return result.Results?.FirstOrDefault();
        }

        public async Task<string?> GetObjectIdAsync(ObjectIdentifiers ids)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();

            var rowKey = ids.SiteId + ids.ListId + ids.ListItemId;
            var filter = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, rowKey);
            var query = new TableQuery<ObjectIdentifiersEntity>().Where(filter);

            var result = await objectIdentifiersTable.ExecuteQuerySegmentedAsync(query, null);
            var objectIdentifiersEntity = result.Results?.FirstOrDefault();
            return objectIdentifiersEntity?.PartitionKey;
        }

        public async Task SaveObjectIdentifiersAsync(string objectId, ObjectIdentifiers ids)
        {
            var objectIdentifiersTable = GetObjectIdentifiersTable();

            var document = new ObjectIdentifiersEntity(objectId, ids);
            await _storageTableService.SaveAsync(objectIdentifiersTable, document);
        }

        public async Task SaveAdditionalIdentifiersAsync(string objectId, List<string> additionalIds)
        {
            var ids = await GetObjectIdentifiersEntityByObjectIdAsync(objectId);
            ids.AdditionalObjectIds = String.Join(seperator, additionalIds);

            var objectIdentifiersTable = GetObjectIdentifiersTable();
            await _storageTableService.SaveAsync(objectIdentifiersTable, ids);
        }
    }
}