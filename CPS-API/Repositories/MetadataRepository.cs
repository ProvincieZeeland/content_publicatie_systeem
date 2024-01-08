using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
using PnP.Framework.Extensions;
using FileInformation = CPS_API.Models.FileInformation;
using ListItem = Microsoft.Graph.ListItem;

namespace CPS_API.Repositories
{
    public interface IMetadataRepository
    {
        Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false);

        Task<FileInformation> GetMetadataAsync(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false);

        Task<FileInformation> GetMetadataWithoutExternalReferencesAsync(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false);

        DropOffFileMetadata GetDropOffMetadata(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false);

        Task<bool> FileContainsMetadata(ObjectIdentifiers ids);

        Task UpdateAllMetadataAsync(FileInformation metadata, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateFileName(string objectId, string fileName, bool getAsUser = false);

        Task UpdateMetadataWithoutExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateAdditionalIdentifiers(FileInformation metadata);

        Task UpdateDropOffMetadataAsync(bool isComplete, string status, FileInformation metadata, bool getAsUser = false);
    }

    public class MetadataRepository : IMetadataRepository
    {
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IDriveRepository _driveRepository;
        private readonly IListRepository _listRepository;
        private readonly TelemetryClient _telemetryClient;
        private readonly ISharePointRepository _sharePointRepository;

        public MetadataRepository(
            IObjectIdRepository objectIdRepository,
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            IDriveRepository driveRepository,
            IListRepository listRepository,
            TelemetryClient telemetryClient,
            ISharePointRepository sharePointRepository)
        {
            _objectIdRepository = objectIdRepository;
            _globalSettings = settings.Value;
            _driveRepository = driveRepository;
            _listRepository = listRepository;
            _telemetryClient = telemetryClient;
            _sharePointRepository = sharePointRepository;
        }

        public async Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false)
        {
            var objectIdentifiers = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (objectIdentifiers == null)
            {
                throw new CpsException("Error while getting objectIdentifiers");
            }
            var ids = new ObjectIdentifiers(objectIdentifiers);

            ListItem? listItem;
            try
            {
                listItem = await _listRepository.GetListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, getAsUser);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw;
            }
            catch (Exception ex) when (ex is MsalUiRequiredException || ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting listItem", ex);
            }
            if (listItem == null) throw new FileNotFoundException($"ListItem (objectId = {objectId}) does not exist!");

            return await GetMetadataAsync(listItem, ids, getAsUser);
        }

        public async Task<FileInformation> GetMetadataAsync(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false)
        {
            var metadata = await GetMetadataWithoutExternalReferencesAsync(listItem, ids, getAsUser);
            metadata.ExternalReferences = await GetExternalReferences(ids, getAsUser);
            return metadata;
        }

        private async Task<List<ExternalReferences>> GetExternalReferences(ObjectIdentifiers ids, bool getAsUser = false)
        {
            var externalReferenceListItems = await GetExternalReferenceListItems(ids, getAsUser);
            var externalReferences = new List<ExternalReferences>();
            foreach (var externalReferenceListItem in externalReferenceListItems)
            {
                var externalReference = new ExternalReferences();
                foreach (var fieldMapping in _globalSettings.ExternalReferencesMapping)
                {
                    if (fieldMapping.FieldName == nameof(ids.ObjectId))
                    {
                        continue;
                    }
                    externalReference[fieldMapping.FieldName] = MetadataHelper.GetMetadataValue(externalReferenceListItem, fieldMapping);
                }
                externalReferences.Add(externalReference);
            }
            return externalReferences;
        }

        public async Task<FileInformation> GetMetadataWithoutExternalReferencesAsync(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false)
        {
            var metadata = new FileInformation();
            metadata.Ids = ids;
            metadata.FileName = await GetItemName(listItem, ids, getAsUser);
            metadata.CreatedOn = MetadataHelper.GetDateTime(listItem.CreatedDateTime);
            metadata.CreatedBy = MetadataHelper.GetUserName(listItem.CreatedBy);
            metadata.ModifiedOn = MetadataHelper.GetDateTime(listItem.LastModifiedDateTime);
            metadata.ModifiedBy = MetadataHelper.GetUserName(listItem.LastModifiedBy);

            metadata.AdditionalMetadata = new FileMetadata();
            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                if (fieldMapping.FieldName == nameof(metadata.Ids.ObjectId))
                {
                    continue;
                }

                var value = MetadataHelper.GetMetadataValue(listItem, fieldMapping);
                if (MetadataHelper.FieldIsMainMetadata(fieldMapping.FieldName))
                {
                    metadata[fieldMapping.FieldName] = value;
                }
                else
                {
                    metadata.AdditionalMetadata[fieldMapping.FieldName] = value;
                }
            }
            return metadata;
        }

        private async Task<string> GetItemName(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false)
        {
            string? itemName = null;
            try
            {
                itemName = GetItemNameByFileLeafRef(listItem);
            }
            catch
            {
                // do nothing > we will use drive item name instead
            }
            if (!string.IsNullOrEmpty(itemName))
            {
                return itemName;
            }

            if (ids.SiteId.IsNullOrEmpty())
            {
                throw new CpsException("Error while getting driveItem, unkown SiteId");
            }
            else if (ids.ListId.IsNullOrEmpty())
            {
                throw new CpsException("Error while getting driveItem, unkown ListId");
            }
            else if (ids.ListItemId.IsNullOrEmpty())
            {
                throw new CpsException("Error while getting driveItem, unkown ListItemId");
            }

            DriveItem? driveItem;
            try
            {
                driveItem = await _driveRepository.GetDriveItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, getAsUser);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting driveItem", ex);
            }

            if (driveItem == null)
            {
                throw new FileNotFoundException($"DriveItem (SiteId = {ids.SiteId}, ListId = {ids.ListId}, ListItemId = {ids.ListItemId}) does not exist!");
            }
            return driveItem.Name;
        }

        private string? GetItemNameByFileLeafRef(ListItem listItem)
        {
            listItem.Fields.AdditionalData.TryGetValue("FileLeafRef", out var fileRef);
            if (fileRef == null)
            {
                return null;
            }
            return fileRef.ToString();
        }

        public DropOffFileMetadata GetDropOffMetadata(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false)
        {
            var metadata = new DropOffFileMetadata();
            foreach (var fieldMapping in _globalSettings.DropOffMetadataMapping)
            {
                // create object with sharepoint fields metadata + url to item
                listItem.Fields.AdditionalData.TryGetValue(fieldMapping.SpoColumnName, out var value);
                if (value == null)
                {
                    // log warning to insights?
                }
                metadata[fieldMapping.FieldName] = value;
            }
            return metadata;
        }

        public async Task UpdateAllMetadataAsync(FileInformation metadata, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            metadata.Ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);

            if (metadata.AdditionalMetadata != null)
            {
                await UpdateAdditionalIdentifiers(metadata);
            }

            await UpdateMetadataWithoutExternalReferencesAsync(metadata, ignoreRequiredFields: ignoreRequiredFields, getAsUser: getAsUser);

            if (metadata.ExternalReferences != null)
            {
                await UpdateExternalReferencesAsync(metadata, ignoreRequiredFields: ignoreRequiredFields, getAsUser: getAsUser);
            }

            if (!string.IsNullOrEmpty(metadata.FileName))
            {
                await UpdateFileName(metadata.Ids.ObjectId, metadata.FileName);
            }
        }

        public async Task UpdateMetadataWithoutExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            // map received metadata to SPO object
            var fields = MapMetadata(metadata, isForNewFile, ignoreRequiredFields);
            if (fields == null) throw new CpsException("Failed to map fields for metadata");

            // update sharepoint fields with metadata
            if (fields.AdditionalData.Count > 0)
            {
                var ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
                await _listRepository.UpdateListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, fields, getAsUser);
            }

            // update terms
            await _sharePointRepository.UpdateTermsForMetadataAsync(metadata, isForNewFile, ignoreRequiredFields, getAsUser);
        }

        public async Task UpdateExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            // Keep the existing value, if value equals null.
            if (metadata.ExternalReferences == null)
            {
                return;
            }

            // map received metadata to SPO object
            var listItems = MapExternalReferences(metadata);
            if (listItems == null) throw new CpsException("Failed to map external references");

            // Get existing sharepoint fields with metadata
            var ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
            var existingListItems = await GetExternalReferenceListItems(metadata.Ids, getAsUser);

            // Check if we need to update the external references.
            var newAndUpdatedLisItemIds = await AddOrUpdateListItems(listItems, metadata, existingListItems, ids, getAsUser);

            // update terms
            await _sharePointRepository.UpdateTermsForExternalReferencesAsync(metadata, newAndUpdatedLisItemIds, isForNewFile, ignoreRequiredFields, getAsUser);
        }

        private async Task<List<string>> AddOrUpdateListItems(List<ListItem> listItems, FileInformation metadata, List<ListItem> existingListItems, ObjectIdentifiers ids, bool getAsUser = false)
        {
            var newAndUpdatedLisItemIds = new List<string>();
            var i = 0;
            foreach (var listItem in listItems)
            {
                var newOrUpdatedListItemId = await AddOrUpdateListItem(listItem, i, metadata, existingListItems, ids, getAsUser);
                newAndUpdatedLisItemIds.Add(newOrUpdatedListItemId);
                i++;
            }
            return newAndUpdatedLisItemIds;
        }

        private async Task<string> AddOrUpdateListItem(ListItem listItem, int index, FileInformation metadata, List<ListItem> existingListItems, ObjectIdentifiers ids, bool getAsUser = false)
        {
            var existingListItem = GetExistingListItem(metadata, existingListItems, index);
            if (existingListItem == null)
            {
                // Add new external reference
                try
                {
                    var newListItem = await _listRepository.AddListItemAsync(ids.SiteId, ids.ExternalReferenceListId, listItem, getAsUser);
                    return newListItem.Id;
                }
                catch (Exception)
                {
                    _telemetryClient.TrackEvent($"Error while adding externalReference (Fields = {JsonSerializer.Serialize(listItem.Fields)})");
                    throw;
                }
            }

            // Update existing external reference
            try
            {
                await _listRepository.UpdateListItemAsync(ids.SiteId, ids.ExternalReferenceListId, existingListItem.Id, listItem.Fields, getAsUser);
                return existingListItem.Id;
            }
            catch (Exception)
            {
                _telemetryClient.TrackEvent($"Error while updating externalReference (Id = {existingListItem.Id}, Fields = {JsonSerializer.Serialize(listItem.Fields)})");
                throw;
            }
        }

        private ListItem? GetExistingListItem(FileInformation metadata, List<ListItem> existingListItems, int index)
        {
            var externalApplicationSpoColumnName = _globalSettings.ExternalReferencesMapping.Find(mapping => mapping.FieldName == nameof(ExternalReferences.ExternalApplication))?.SpoColumnName;
            return existingListItems.Find(item =>
                item.Fields.AdditionalData.TryGetValue(externalApplicationSpoColumnName, out var value)
                && value != null
                && metadata.ExternalReferences[index].ExternalApplication == JsonSerializer.Deserialize<TaxonomyItemDto>(value.ToString())?.Label
            );
        }

        public async Task UpdateFileName(string objectId, string fileName, bool getAsUser = false)
        {
            if (objectId == null) throw new ArgumentNullException("objectId");
            if (fileName == null) throw new ArgumentNullException("fileName");

            // Get SharePoint ID's
            var ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (ids == null) throw new CpsException("Error while getting sharePointIds");

            // Update fileName
            var driveItem = new DriveItem
            {
                Name = fileName
            };
            await _driveRepository.UpdateDriveItemAsync(ids.DriveId, ids.DriveItemId, driveItem, getAsUser);

            // Update mimetype
            new FileExtensionContentTypeProvider().TryGetContentType(fileName, out var mimeType);

            var fields = new FieldValueSet();
            fields.AdditionalData = new Dictionary<string, object>();
            if (mimeType != null)
            {
                var mapping = _globalSettings.MetadataMapping.Find(mapping => mapping.FieldName == nameof(FileInformation.MimeType));
                if (mapping != null)
                {
                    fields.AdditionalData[mapping.SpoColumnName] = mimeType;
                }
            }

            // Update fileExtension
            var fileExtension = Path.GetExtension(fileName).Replace(".", "");

            var fieldMapping = _globalSettings.MetadataMapping.Find(mapping => mapping.FieldName == nameof(FileInformation.FileExtension));
            if (fieldMapping != null)
            {
                fields.AdditionalData[fieldMapping.SpoColumnName] = fileExtension;
            }

            // Update title
            var title = Path.GetFileNameWithoutExtension(fileName);

            fieldMapping = _globalSettings.MetadataMapping.Find(mapping => mapping.FieldName == nameof(FileMetadata.Title));
            if (fieldMapping != null)
            {
                fields.AdditionalData[fieldMapping.SpoColumnName] = title;
            }

            // update sharepoint fields with metadata
            await _listRepository.UpdateListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, fields, getAsUser);
        }

        public async Task UpdateDropOffMetadataAsync(bool isComplete, string status, FileInformation metadata, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            // map received metadata to SPO object
            var fields = MapMetadata(metadata, true, true);
            if (fields == null) throw new CpsException("Failed to map fields for metadata");
            var dropOffMetadata = new DropOffFileMetadata(isComplete, status);
            var dropOffFields = MapDropOffMetadata(dropOffMetadata);
            if (dropOffFields == null) throw new CpsException("Failed to map fields for metadata");
            fields.AdditionalData.AddRange(dropOffFields.AdditionalData);

            // update sharepoint fields with metadata
            if (fields.AdditionalData.Count > 0)
            {
                var ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
                await _listRepository.UpdateListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, fields, getAsUser);
            }
        }

        public async Task UpdateAdditionalIdentifiers(FileInformation metadata)
        {
            try
            {
                var additionalObjectIds = MapAdditionalIds(metadata);
                if (!string.IsNullOrEmpty(additionalObjectIds))
                {
                    await _objectIdRepository.SaveAdditionalIdentifiersAsync(metadata.Ids.ObjectId, additionalObjectIds);
                }
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while updating additional IDs", ex);
            }
        }

        public async Task<bool> FileContainsMetadata(ObjectIdentifiers ids)
        {
            var listItem = await _listRepository.GetListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId);
            if (listItem == null) throw new CpsException($"Error while getting listItem (SiteId = \"{ids.SiteId}\", ListId = \"{ids.ListId}\", ListItemId = \"{ids.ListItemId}\")");

            // When metadata is unknown, we skip the synchronisation.
            // The file is a new incomplete file or something went wrong while adding the file.
            var additionalData = listItem.Fields.AdditionalData;
            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                var succeeded = additionalData.TryGetValue(fieldMapping.SpoColumnName, out var value);
                if (!succeeded)
                {
                    continue;
                }
                if (value != null)
                {
                    var defaultValue = fieldMapping.DefaultValue;
                    if (defaultValue == null)
                    {
                        return true;
                    }

                    if (fieldMapping.FieldName == nameof(ids.ObjectId))
                    {
                        continue;
                    }

                    var propertyInfo = GetPropertyInfo(fieldMapping);
                    if (propertyInfo == null) throw new CpsException("Error while getting type of metadata");

                    if (PropertyContainsData(value, defaultValue, propertyInfo))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private PropertyInfo GetPropertyInfo(FieldMapping fieldMapping)
        {
            var tempMetadata = new FileMetadata();
            var propertyInfo = tempMetadata.GetType().GetProperty(fieldMapping.FieldName);
            if (propertyInfo == null)
            {
                var tempFileInfo = new FileInformation();
                return tempFileInfo.GetType().GetProperty(fieldMapping.FieldName);
            }
            return propertyInfo;
        }

        private bool PropertyContainsData(object? value, object? defaultValue, PropertyInfo propertyInfo)
        {
            if (propertyInfo.PropertyType == typeof(int))
            {
                if (IntegerPropertyContainsData(value, defaultValue))
                {
                    return true;
                }
            }
            else if (propertyInfo.PropertyType == typeof(DateTime))
            {
                if (DateTimePropertyContainsData(value, defaultValue))
                {
                    return true;
                }
            }
            else if (propertyInfo.PropertyType == typeof(string))
            {
                if (StringPropertyContainsData(value, defaultValue))
                {
                    return true;
                }
            }
            else if (value != defaultValue)
            {
                return true;
            }
            return false;
        }

        private static bool IntegerPropertyContainsData(object? value, object? defaultValue)
        {
            var stringValue = value.ToString();
            var stringDefaultValue = defaultValue.ToString();
            var decimalValue = Convert.ToDecimal(stringValue, new CultureInfo("en-US"));
            var decimalDefaultValue = Convert.ToDecimal(stringDefaultValue, new CultureInfo("en-US"));
            if (decimalValue != decimalDefaultValue)
            {
                return true;
            }
            return false;
        }

        private static bool DateTimePropertyContainsData(object? value, object? defaultValue)
        {
            var stringValue = value.ToString();
            var dateParsed = DateTime.TryParse(stringValue, out DateTime dateTimeValue);
            DateTime? nullableDateValue = null;
            if (dateParsed)
            {
                nullableDateValue = dateTimeValue;
            }
            var stringDefaultValue = defaultValue.ToString();
            dateParsed = DateTime.TryParse(stringDefaultValue, out DateTime dateTimeDefaultValue);
            DateTime? nullableDateDefaultValue = null;
            if (dateParsed)
            {
                nullableDateDefaultValue = dateTimeDefaultValue;
            }
            if (nullableDateValue != nullableDateDefaultValue)
            {
                return true;
            }
            return false;
        }

        private static bool StringPropertyContainsData(object? value, object? defaultValue)
        {
            var stringValue = value.ToString();
            var stringDefaultValue = defaultValue.ToString();
            if (stringValue != stringDefaultValue)
            {
                return true;
            }
            return false;
        }

        #region Map data

        private FieldValueSet MapMetadata(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            var fields = new FieldValueSet
            {
                AdditionalData = new Dictionary<string, object>()
            };
            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                try
                {
                    if (!MetadataHelper.IsEditFieldAllowed(fieldMapping, isForNewFile))
                    {
                        continue;
                    }

                    var value = MetadataHelper.GetMetadataValue(metadata, fieldMapping);
                    var propertyInfo = MetadataHelper.GetMetadataPropertyInfo(metadata, fieldMapping);
                    if (MetadataHelper.KeepExistingValue(value, propertyInfo, isForNewFile, ignoreRequiredFields))
                    {
                        continue;
                    }

                    fields.AdditionalData[fieldMapping.SpoColumnName] = GetValue(fieldMapping, value, propertyInfo, isForNewFile, ignoreRequiredFields);
                }
                catch (FieldRequiredException)
                {
                    throw;
                }
                catch
                {
                    throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
                }
            }
            return fields;
        }

        private FieldValueSet MapDropOffMetadata(DropOffFileMetadata dropOffMetadata)
        {
            var fields = new FieldValueSet
            {
                AdditionalData = new Dictionary<string, object>()
            };
            foreach (var fieldMapping in _globalSettings.DropOffMetadataMapping)
            {
                try
                {
                    // Only allow updatable fields
                    if (fieldMapping.ReadOnly)
                    {
                        continue;
                    }

                    var value = dropOffMetadata[fieldMapping.FieldName];
                    var propertyInfo = dropOffMetadata.GetType().GetProperty(fieldMapping.FieldName);

                    fields.AdditionalData[fieldMapping.SpoColumnName] = GetValue(fieldMapping, value, propertyInfo);
                }
                catch (FieldRequiredException)
                {
                    throw;
                }
                catch
                {
                    throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
                }
            }
            return fields;
        }

        private object? GetValue(FieldMapping fieldMapping, object? value, PropertyInfo? propertyInfo, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            // Get default value
            var defaultValue = MetadataHelper.GetMetadataDefaultValue(value, propertyInfo, fieldMapping, isForNewFile, ignoreRequiredFields);
            if (defaultValue != null)
            {
                value = defaultValue;
            }

            if (propertyInfo.PropertyType == typeof(DateTime?))
            {
                var stringValue = value?.ToString();
                var dateParsed = DateTime.TryParse(stringValue, out DateTime dateValue);
                if (!dateParsed && !ignoreRequiredFields && fieldMapping.Required)
                {
                    throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
                }

                if (dateValue == DateTime.MinValue)
                {
                    return null;
                }
                return dateValue.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
            }
            return value;
        }

        private List<ListItem> MapExternalReferences(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");
            if (metadata.ExternalReferences == null) throw new ArgumentNullException("metadata.ExternalReferences");

            var listItems = new List<ListItem>();
            foreach (var externalReference in metadata.ExternalReferences)
            {
                var fields = new FieldValueSet
                {
                    AdditionalData = new Dictionary<string, object>()
                };
                foreach (var fieldMapping in _globalSettings.ExternalReferencesMapping)
                {
                    try
                    {
                        if (!MetadataHelper.IsEditFieldAllowed(fieldMapping, isForNewFile))
                        {
                            continue;
                        }

                        object? value;
                        PropertyInfo propertyInfo;
                        if (fieldMapping.FieldName == nameof(metadata.Ids.ObjectId))
                        {
                            value = metadata.Ids.ObjectId;
                            propertyInfo = metadata.Ids.GetType().GetProperty(fieldMapping.FieldName);
                        }
                        else
                        {
                            value = externalReference[fieldMapping.FieldName];
                            propertyInfo = externalReference.GetType().GetProperty(fieldMapping.FieldName);
                        }

                        if (MetadataHelper.KeepExistingValue(value, propertyInfo, isForNewFile, ignoreRequiredFields))
                        {
                            continue;
                        }

                        var defaultValue = MetadataHelper.GetMetadataDefaultValue(value, propertyInfo, fieldMapping, isForNewFile, ignoreRequiredFields);
                        if (defaultValue != null)
                        {
                            value = defaultValue;
                        }
                        if (MetadataHelper.IsMetadataFieldEmpty(value, propertyInfo))
                        {
                            continue;
                        }

                        fields.AdditionalData[fieldMapping.SpoColumnName] = value;
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName, ex);
                    }
                }
                listItems.Add(new ListItem { Fields = fields });
            }
            return listItems;
        }

        private string MapAdditionalIds(FileInformation metadata)
        {
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException("metadata.AdditionalMetadata");

            try
            {
                if (!string.IsNullOrEmpty(_globalSettings.AdditionalObjectId)
                    && metadata.AdditionalMetadata != null
                    && metadata.AdditionalMetadata[_globalSettings.AdditionalObjectId] != null)
                {
                    var id = metadata.AdditionalMetadata[_globalSettings.AdditionalObjectId].ToString();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Cannot parse additional ids", _globalSettings.AdditionalObjectId, ex);
            }

            return string.Empty;
        }

        #endregion Map data

        #region Get data / Fields

        private async Task<List<ListItem>?> GetExternalReferenceListItems(ObjectIdentifiers ids, bool getAsUser = false)
        {
            var field = _globalSettings.ExternalReferencesMapping.Find(s => s.FieldName.Equals("ObjectID", StringComparison.InvariantCultureIgnoreCase));
            if (field == null) throw new CpsException("Object ID field not found in external reference mapping");

            return await _listRepository.GetListItemsAsync(ids.SiteId, ids.ExternalReferenceListId, field.SpoColumnName, ids.ObjectId, getAsUser);
        }

        #endregion Get data / Fields
    }
}