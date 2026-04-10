using System.Net;
using CPS_API.Database;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.ODataErrors;
using Constants = CPS_API.Models.Constants;
using ObjectIdentifiers = CPS_API.Models.ObjectIdentifiers;

namespace CPS_API.Repositories
{
    public interface IObjectIdRepository
    {
        Task<string> GenerateObjectIdAsync(ObjectIdentifiers ids, bool getAsUser = false);

        Task<ObjectIdentifiers> GetObjectIdentifiersAsync(string siteId, string listId, string listItemId);

        Task<ObjectIdentifiers?> GetObjectIdentifiersAsync(string driveId, string driveItemId);

        Task<ObjectIdentifiers?> GetObjectIdentifiersAsync(string objectId);

        Task<ObjectIdentifiers?> GetObjectIdentifiersBySharePointIdsAsync(string siteId, string listId, string listItemId);

        Task<string?> GetObjectIdAsync(ObjectIdentifiers ids);

        Task SaveObjectIdentifiersAsync(string objectId, ObjectIdentifiers ids);

        Task UpdateObjectIdentifiersAsync(string objectId, ObjectIdentifiers ids);

        Task SaveAdditionalIdentifiersAsync(string objectId, string additionalIds);

        Task<ObjectIdentifiers> FindMissingIds(ObjectIdentifiers ids, bool getAsUser = false);

        string? GetExternalReferenceListId(ObjectIdentifiers ids);
    }

    public class ObjectIdRepository : IObjectIdRepository
    {
        private const string prefix = "ZLD";
        private readonly ISettingsRepository _settingsRepository;
        private readonly IDriveRepository _driveRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly CpsDbContext _dbContext;
        private readonly IDatabaseHealthService _databaseHealthService;

        public ObjectIdRepository(ISettingsRepository settingsRepository,
                                   IDriveRepository driveRepository,
                                   IOptions<GlobalSettings> settings,
                                   CpsDbContext dbContext,
                                   IDatabaseHealthService databaseHealthService)
        {
            _settingsRepository = settingsRepository;
            _driveRepository = driveRepository;
            _globalSettings = settings.Value;
            _dbContext = dbContext;
            _databaseHealthService = databaseHealthService;
        }

        public async Task<string> GenerateObjectIdAsync(ObjectIdentifiers ids, bool getAsUser = false)
        {
            // Add any missing location IDs before looking for existing.
            ids = await FindMissingIds(ids, getAsUser);

            // Check if the ID's are valid.
            if (string.IsNullOrEmpty(ids.SiteId))
            {
                throw new CpsException(nameof(ids.SiteId) + " not found");
            }
            if (string.IsNullOrEmpty(ids.ListId))
            {
                throw new CpsException(nameof(ids.ListId) + " not found");
            }
            if (string.IsNullOrEmpty(ids.ListItemId))
            {
                throw new CpsException(nameof(ids.ListItemId) + " not found");
            }
            if (string.IsNullOrEmpty(ids.DriveId))
            {
                throw new CpsException(nameof(ids.DriveId) + " not found");
            }
            if (string.IsNullOrEmpty(ids.DriveItemId))
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
            ids = await FindMissingIdsBySharePointIds(ids, getAsUser);
            ids = await FindMissingIdsByDriveIds(ids, getAsUser);
            ids = await FindMissingIdsFromStorageTable(ids);
            if (string.IsNullOrWhiteSpace(ids.ObjectId) && !string.IsNullOrWhiteSpace(ids.SiteId) && !string.IsNullOrWhiteSpace(ids.ListId) && !string.IsNullOrWhiteSpace(ids.ListItemId))
            {
                ids.ObjectId = await GetObjectIdAsync(ids) ?? string.Empty;
            }
            return ids;
        }

        private async Task<ObjectIdentifiers> FindMissingIdsBySharePointIds(ObjectIdentifiers ids, bool getAsUser)
        {
            if (!string.IsNullOrEmpty(ids.DriveId) && !string.IsNullOrEmpty(ids.DriveItemId))
            {
                // Ids already found
                return ids;
            }
            if (string.IsNullOrEmpty(ids.SiteId) || string.IsNullOrEmpty(ids.ListId))
            {
                // Not all required ids present
                return ids;
            }

            // Find driveID for object
            ids.DriveId = await FindMissingDriveIdBySharePointIds(ids, getAsUser);

            // Find driveItemID for object
            if (string.IsNullOrEmpty(ids.ListItemId))
            {
                // Required id present
                return ids;
            }
            ids.DriveItemId = await FindMissingDriveItemIdBySharePointIds(ids, getAsUser);
            return ids;
        }

        private async Task<string> FindMissingDriveIdBySharePointIds(ObjectIdentifiers ids, bool getAsUser)
        {
            if (string.IsNullOrEmpty(ids.SiteId)) throw new CpsException($"No {nameof(ObjectIdentifiers.SiteId)} found for {nameof(FileInformation.Ids)}");
            if (string.IsNullOrEmpty(ids.ListId)) throw new CpsException($"No {nameof(ObjectIdentifiers.ListId)} found for {nameof(FileInformation.Ids)}");

            try
            {
                var driveId = await _driveRepository.GetDriveIdAsync(ids.SiteId!, ids.ListId!, getAsUser);
                if (string.IsNullOrWhiteSpace(driveId)) throw new CpsException($"Error while getting driveId (SiteId = {ids.SiteId}, ListId = {ids.ListId})");
                return driveId;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.BadRequest && (ex.Error == null || ex.Error.Message == null || ex.Error.Message.Equals(Constants.ODataErrors.InvalidHostnameForThisTenancy, StringComparison.InvariantCultureIgnoreCase)))
            {
                throw new FileNotFoundException("The specified site was not found", ex);
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"Drive (SiteId = {ids.SiteId}, ListId = {ids.ListId}) does not exist!");
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting driveId", ex);
            }
        }

        private async Task<string> FindMissingDriveItemIdBySharePointIds(ObjectIdentifiers ids, bool getAsUser)
        {
            if (string.IsNullOrEmpty(ids.SiteId)) throw new CpsException($"No {nameof(ObjectIdentifiers.SiteId)} found for {nameof(FileInformation.Ids)}");
            if (string.IsNullOrEmpty(ids.ListId)) throw new CpsException($"No {nameof(ObjectIdentifiers.ListId)} found for {nameof(FileInformation.Ids)}");
            if (string.IsNullOrEmpty(ids.ListItemId)) throw new CpsException($"No {nameof(ObjectIdentifiers.ListItemId)} found for {nameof(FileInformation.Ids)}");

            try
            {
                var driveItem = await _driveRepository.GetDriveItemAsync(ids.SiteId!, ids.ListId!, ids.ListItemId!, getAsUser: getAsUser);
                if (string.IsNullOrWhiteSpace(driveItem.Id)) throw new CpsException($"Error while getting driveItem: driveItem ID unknown");
                return driveItem.Id;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"DriveItem (SiteId = {ids.SiteId}, ListId = {ids.ListId}, ListItemId = {ids.ListItemId}) does not exist!");
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting driveItemId", ex);
            }
        }

        private async Task<ObjectIdentifiers> FindMissingIdsByDriveIds(ObjectIdentifiers ids, bool getAsUser)
        {
            if (!string.IsNullOrEmpty(ids.SiteId) && !string.IsNullOrEmpty(ids.ListId) && !string.IsNullOrEmpty(ids.ListItemId))
            {
                // Ids already found
                return ids;
            }
            if (string.IsNullOrEmpty(ids.DriveId) || string.IsNullOrEmpty(ids.DriveItemId))
            {
                // Not all required ids present
                return ids;
            }

            try
            {
                var driveItem = await _driveRepository.GetDriveItemIdsAsync(ids.DriveId, ids.DriveItemId, getAsUser);
                ids.SiteId = driveItem.SharepointIds!.SiteId!;
                ids.ListId = driveItem.SharepointIds.ListId!;
                ids.ListItemId = driveItem.SharepointIds.ListItemId!;
                return ids;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.BadRequest && (ex.Error == null || ex.Error.Message == null || ex.Error.Message.Equals(Constants.ODataErrors.ProvidedDriveIdMalformed, StringComparison.InvariantCultureIgnoreCase)))
            {
                throw new FileNotFoundException("The specified drive was not found", ex);
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"DriveItem (DriveId = {ids.DriveId}, DriveItemId = {ids.DriveItemId}) does not exist!");
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting SharePoint Ids", ex);
            }
        }

        private async Task<ObjectIdentifiers> FindMissingIdsFromStorageTable(ObjectIdentifiers ids)
        {
            if (!string.IsNullOrEmpty(ids.SiteId) && !string.IsNullOrEmpty(ids.ListId) && !string.IsNullOrEmpty(ids.ListItemId)
                && !string.IsNullOrEmpty(ids.DriveId) && !string.IsNullOrEmpty(ids.DriveItemId))
            {
                // Ids already found
                return ids;
            }
            if (string.IsNullOrEmpty(ids.ObjectId))
            {
                // Not all required ids present
                return ids;
            }
            try
            {
                var idsFromStorageTable = await this.GetObjectIdentifiersAsync(ids.ObjectId);
                if (idsFromStorageTable == null) throw new CpsException("Identifiers not found");
                return idsFromStorageTable;
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting SharePoint Ids", ex);
            }
        }

        public string? GetExternalReferenceListId(ObjectIdentifiers ids)
        {
            var locationMapping = _globalSettings.LocationMapping.Find(item =>
                item.SiteId == ids.SiteId
                && item.ListId == ids.ListId
            );
            return locationMapping?.ExternalReferenceListId;
        }

        public async Task<ObjectIdentifiers> GetObjectIdentifiersAsync(string siteId, string listId, string listItemId)
        {
            var ids = new ObjectIdentifiers();
            ids.SiteId = siteId;
            ids.ListId = listId;
            ids.ListItemId = listItemId;

            // Get all ids for current location
            return await FindMissingIds(ids);
        }

        public async Task<ObjectIdentifiers?> GetObjectIdentifiersAsync(string driveId, string driveItemId)
        {
            return await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.ObjectIdentifiers.FirstOrDefaultAsync(
                    oi => oi.DriveId == driveId && oi.DriveItemId == driveItemId),
                nameof(GetObjectIdentifiersAsync)
            );
        }

        public async Task<ObjectIdentifiers?> GetObjectIdentifiersAsync(string objectId)
        {
            ObjectIdentifiers? objectIdentifiers = null;
            if (!string.IsNullOrEmpty(_globalSettings.AdditionalObjectId))
                objectIdentifiers = await GetObjectIdentifiersByAdditionalIdsAsync(objectId);

            if (objectIdentifiers == null)
                objectIdentifiers = await GetObjectIdentifiersByObjectIdAsync(objectId);

            if (objectIdentifiers == null)
                throw new FileNotFoundException($"ObjectIdentifiers (objectId = {objectId}) does not exist!");

            return objectIdentifiers;
        }

        private async Task<ObjectIdentifiers?> GetObjectIdentifiersByAdditionalIdsAsync(string objectId)
        {
            ArgumentNullException.ThrowIfNull(objectId);
            objectId = objectId.ToUpper().Trim();

            return await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.ObjectIdentifiers
                    .FirstOrDefaultAsync(oi => oi.AdditionalObjectId != null && oi.AdditionalObjectId.Equals(objectId)),
                nameof(GetObjectIdentifiersByAdditionalIdsAsync)
            );
        }

        private async Task<ObjectIdentifiers?> GetObjectIdentifiersByObjectIdAsync(string objectId)
        {
            ArgumentNullException.ThrowIfNull(objectId);
            objectId = objectId.ToUpper().Trim();

            return await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.ObjectIdentifiers
                    .FirstOrDefaultAsync(oi => oi.ObjectId.Equals(objectId)),
                nameof(GetObjectIdentifiersByObjectIdAsync)
            );
        }

        public async Task<ObjectIdentifiers?> GetObjectIdentifiersBySharePointIdsAsync(string siteId, string listId, string listItemId)
        {
            ArgumentNullException.ThrowIfNull(siteId);
            ArgumentNullException.ThrowIfNull(listId);
            ArgumentNullException.ThrowIfNull(listItemId);

            return await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.ObjectIdentifiers
                    .FirstOrDefaultAsync(oi => oi.SiteId.Equals(siteId) && oi.ListId.Equals(listId) && oi.ListItemId.Equals(listItemId)),
                nameof(GetObjectIdentifiersBySharePointIdsAsync)
            );
        }

        public async Task<string?> GetObjectIdAsync(ObjectIdentifiers ids)
        {
            var storedIds = await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.ObjectIdentifiers
                    .FirstOrDefaultAsync(oi => oi.SiteId.Equals(ids.SiteId) && oi.ListId.Equals(ids.ListId) && oi.ListItemId.Equals(ids.ListItemId)),
                nameof(GetObjectIdAsync)
            );
            return storedIds?.ObjectId;
        }

        public async Task SaveObjectIdentifiersAsync(string objectId, ObjectIdentifiers ids)
        {
            var storedIds = await GetObjectIdentifiersByObjectIdAsync(objectId);
            if (storedIds != null) throw new ObjectIdAlreadyExistsException($"File with objectId \"{objectId}\" already exists");

            if (!string.IsNullOrEmpty(ids.AdditionalObjectId))
            {
                ids.AdditionalObjectId = ids.AdditionalObjectId.ToUpper().Trim();
            }
            await _dbContext.ObjectIdentifiers.AddAsync(ids);
            await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.SaveChangesAsync(),
                nameof(SaveObjectIdentifiersAsync)
            );
        }

        public async Task UpdateObjectIdentifiersAsync(string objectId, ObjectIdentifiers ids)
        {
            var storedIds = await GetObjectIdentifiersByObjectIdAsync(objectId);
            if (storedIds == null) throw new CpsException($"No existing objectIdentifiers found for {objectId}");

            if (!string.IsNullOrEmpty(ids.AdditionalObjectId))
            {
                storedIds.AdditionalObjectId = ids.AdditionalObjectId.ToUpper().Trim();
            }
            storedIds.SiteId = ids.SiteId;
            storedIds.ListId = ids.ListId;
            storedIds.ListItemId = ids.ListItemId;
            storedIds.DriveId = ids.DriveId;
            storedIds.DriveItemId = ids.DriveItemId;
            await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.SaveChangesAsync(),
                nameof(UpdateObjectIdentifiersAsync)
            );
        }

        public async Task SaveAdditionalIdentifiersAsync(string objectId, string additionalIds)
        {
            var storedIds = await GetObjectIdentifiersByAdditionalIdsAsync(additionalIds);
            if (storedIds != null) throw new ObjectIdAlreadyExistsException($"File with additionalObjectId \"{additionalIds}\" already exists");

            var ids = await GetObjectIdentifiersByObjectIdAsync(objectId);
            if (ids == null) throw new FileNotFoundException($"ObjectIdentifiersEntity (objectId = {objectId}) does not exist!");

            try
            {
                ids.AdditionalObjectId = additionalIds?.ToUpper().Trim();
                await _databaseHealthService.ExecuteWithWarmupAsync(
                    async () => await _dbContext.SaveChangesAsync(),
                    nameof(SaveAdditionalIdentifiersAsync)
                );
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while updating additional IDs", ex);
            }
        }
    }
}